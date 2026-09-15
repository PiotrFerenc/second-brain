using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

public class OpenAiTagCleaner(IHttpClientFactory httpClientFactory, IOptions<OpenAiOptions> options) : ITagCleaner
{
    // ponytail: grupowanie tagow to ekstrakcja/kategoryzacja, nie twarde rozumowanie jak
    // wykrywanie sprzecznosci - CompressionModel wystarcza, nie trzeba osobnego *Model
    // (patrz PLAN.md decyzje: ConflictModel/AgentModel istnieja bo gpt-3.5-turbo konkretnie
    // zawodzil na tamtym zadaniu, to tu nie zaobserwowano).
    private const string SystemPrompt =
        "Dostajesz liste WSZYSTKICH tagow uzywanych w osobistej bazie notatek uzytkownika. " +
        "Znajdz grupy tagow ktore znacza to samo (liczba pojedyncza/mnoga, oczywiste literowki, " +
        "synonimy) i zasugeruj jedna kanoniczna forme dla kazdej grupy. Pomin tagi ktore nie maja " +
        "duplikatu - nie twórz grup jednoelementowych. Zwroc WYLACZNIE obiekt JSON o jednym polu " +
        "\"groups\": tablica obiektow {\"tags\": [...], \"suggestedCanonical\": \"...\"}. Jesli nie " +
        "ma zadnych duplikatow, zwroc {\"groups\": []}.";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly OpenAiOptions _options = options.Value;

    public async Task<IReadOnlyList<TagGroup>> FindDuplicateGroupsAsync(IReadOnlyList<string> allTags, CancellationToken ct = default)
    {
        if (allTags.Count < 2)
            return [];

        var client = httpClientFactory.CreateClient("OpenAI");
        var response = await client.PostAsJsonAsync("chat/completions", new
        {
            model = _options.CompressionModel,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = string.Join(", ", allTags) }
            }
        }, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Pusta odpowiedz OpenAI chat/completions.");

        var raw = body.Choices[0].Message.Content.Trim();
        var parsed = JsonSerializer.Deserialize<GroupsResult>(raw, JsonOptions)
            ?? throw new InvalidOperationException($"Nie udalo sie sparsowac JSON z czyszczenia tagow: {raw}");

        return parsed.Groups ?? [];
    }

    private record GroupsResult([property: JsonPropertyName("groups")] TagGroup[]? Groups);
    private record OpenAiChatResponse([property: JsonPropertyName("choices")] OpenAiChoice[] Choices);
    private record OpenAiChoice([property: JsonPropertyName("message")] OpenAiMessage Message);
    private record OpenAiMessage([property: JsonPropertyName("content")] string Content);
}

// Wykonuje faktyczne scalenie: przepisuje pliki notatek (przez INoteStore.MergeTagsAsync)
// i doupsertowuje kazda zmieniona notatke do Qdrant (tagi sa tez w payloadzie wyszukiwania,
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
