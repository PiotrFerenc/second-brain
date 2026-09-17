using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

public class FabrykaTagCleaner(IHttpClientFactory httpClientFactory, IOptions<TagCleaningOptions> options) : ITagCleaner
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
