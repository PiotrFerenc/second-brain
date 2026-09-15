using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

public class FabrykaConflictDetector(IHttpClientFactory httpClientFactory, IOptions<ConflictDetectionOptions> options) : IConflictDetector
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ConflictDetectionOptions _options = options.Value;

    public async Task<ConflictResult> DetectAsync(string newContent, IReadOnlyList<Note> candidates, CancellationToken ct = default)
    {
        if (candidates.Count == 0)
            return new ConflictResult(false, null, null);

        var context = string.Join("\n\n---\n\n", candidates.Select((n, i) => $"Notatka {i + 1} (\"{n.Title}\"): {n.CompressedContent}"));
        var userPrompt = $"NOWA notatka: {newContent}\n\nJUZ ISTNIEJACE notatki:\n{context}";

        var client = httpClientFactory.CreateClient("ConflictDetection");
        var response = await client.PostAsJsonAsync("chat/completions", new
        {
            model = _options.Model,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = _options.SystemPrompt },
                new { role = "user", content = userPrompt }
            }
        }, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<FabrykaChatResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Pusta odpowiedz Fabryka chat/completions.");

        var raw = body.Choices[0].Message.Content.Trim();
        return JsonSerializer.Deserialize<ConflictResult>(raw, JsonOptions)
            ?? throw new InvalidOperationException($"Nie udalo sie sparsowac JSON z wykrywacza sprzecznosci: {raw}");
    }

    private record FabrykaChatResponse([property: JsonPropertyName("choices")] FabrykaChoice[] Choices);
    private record FabrykaChoice([property: JsonPropertyName("message")] FabrykaMessage Message);
    private record FabrykaMessage([property: JsonPropertyName("content")] string Content);
}
