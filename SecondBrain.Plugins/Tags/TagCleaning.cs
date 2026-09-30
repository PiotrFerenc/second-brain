using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SecondBrain.Core;
using SecondBrain.Infrastructure;

namespace SecondBrain.Plugins.Tags;

// Sugestia grupy tagow-duplikatow (np. "spotkanie"/"spotkania") do recznego scalenia.
public record TagGroup(string[] Tags, string SuggestedCanonical);

public class TagCleaningOptions : HttpClientOptions
{
    // ponytail: grupowanie tagow to ekstrakcja/kategoryzacja, nie twarde rozumowanie jak
    // wykrywanie sprzecznosci - domyslny model jak w Compression wystarcza (patrz PLAN.md
    // decyzje: ConflictModel/AgentModel istnieja bo gpt-3.5-turbo konkretnie zawodzil na
    // tamtym zadaniu, to tu nie zaobserwowano). Konfiguracja mimo to osobna, jak kazdy provider.
    public string Model { get; set; } = "gpt-3.5-turbo";

    public string SystemPrompt { get; set; } =
        "Dostajesz liste WSZYSTKICH tagow uzywanych w osobistej bazie notatek uzytkownika. " +
        "Znajdz grupy tagow ktore znacza to samo (liczba pojedyncza/mnoga, oczywiste literowki, " +
        "synonimy) i zasugeruj jedna kanoniczna forme dla kazdej grupy. Pomin tagi ktore nie maja " +
        "duplikatu - nie twórz grup jednoelementowych. Zwroc WYLACZNIE obiekt JSON o jednym polu " +
        "\"groups\": tablica obiektow {\"tags\": [...], \"suggestedCanonical\": \"...\"}. Jesli nie " +
        "ma zadnych duplikatow, zwroc {\"groups\": []}.";
}

// Grupuje semantycznie zduplikowane tagi z calej bazy (liczba pojedyncza/mnoga, literowki,
// synonimy) i sugeruje jedna kanoniczna forme na grupe - do recznego scalenia przez usera.
// Jedna implementacja, jeden uzytkownik (ten plugin) - bez interfejsu.

public class TagCleaner(IHttpClientFactory httpClientFactory, IOptions<TagCleaningOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly TagCleaningOptions _options = options.Value;

    public async Task<IReadOnlyList<TagGroup>> FindDuplicateGroupsAsync(IReadOnlyList<string> allTags, CancellationToken ct = default)
    {
        if (allTags.Count < 2)
            return [];

        var client = httpClientFactory.CreateClient("TagCleaning");
        var response = await client.PostAsJsonAsync("chat/completions", new
        {
            model = _options.Model,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = _options.SystemPrompt },
                new { role = "user", content = string.Join(", ", allTags) }
            }
        }, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<FabrykaChatResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Pusta odpowiedz Fabryka chat/completions.");

        var raw = body.Choices[0].Message.Content.Trim();
        var parsed = JsonSerializer.Deserialize<GroupsResult>(raw, JsonOptions)
            ?? throw new InvalidOperationException($"Nie udalo sie sparsowac JSON z czyszczenia tagow: {raw}");

        return parsed.Groups ?? [];
    }

    private record GroupsResult([property: JsonPropertyName("groups")] TagGroup[]? Groups);
    private record FabrykaChatResponse([property: JsonPropertyName("choices")] FabrykaChoice[] Choices);
    private record FabrykaChoice([property: JsonPropertyName("message")] FabrykaMessage Message);
    private record FabrykaMessage([property: JsonPropertyName("content")] string Content);
}

// Wykonuje faktyczne scalenie: przepisuje pliki notatek (przez INoteStore.MergeTagsAsync)
// i doupsertowuje kazda zmieniona notatke do indeksu wektorowego (tagi sa tez w payloadzie wyszukiwania,
// wiec bez tego wyniki search/filter pokazywalyby stary tag). Konkretna klasa, jedna
// implementacja, nie jest mockowana ani bindowana w XAML - bez interfejsu, jak GapAutoCloser.
public class TagMerger(INoteStore noteStore, IVectorIndex vectorIndex, IEmbedder embedder)
{
    public async Task<int> MergeAsync(string[] fromTags, string toTag, CancellationToken ct = default)
    {
        var updated = await noteStore.MergeTagsAsync(fromTags, toTag, ct);

        foreach (var (folder, note) in updated)
        {
            var vector = await embedder.EmbedAsync(note.CompressedContent, ct);
            await vectorIndex.UpsertAsync(folder, note, vector, ct);
        }

        return updated.Count;
    }
}
