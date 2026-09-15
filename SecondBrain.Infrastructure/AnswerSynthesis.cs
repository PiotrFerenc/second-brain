using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

public class FabrykaAnswerSynthesizer(IHttpClientFactory httpClientFactory, IOptions<AnswerSynthesisOptions> options) : IAnswerSynthesizer
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly AnswerSynthesisOptions _options = options.Value;

    public async Task<AnswerResult> SynthesizeAsync(string query, IReadOnlyList<Note> notes, CancellationToken ct = default)
    {
        var context = string.Join("\n\n---\n\n", notes.Select((n, i) => $"Notatka {i + 1}: {n.Title}\n{n.RawContent}"));
        var userPrompt = $"Notatki:\n{context}\n\nPytanie: {query}";

        var client = httpClientFactory.CreateClient("AnswerSynthesis");
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
        return JsonSerializer.Deserialize<AnswerResult>(raw, JsonOptions)
            ?? throw new InvalidOperationException($"Nie udalo sie sparsowac JSON z odpowiedzi: {raw}");
    }

    private record FabrykaChatResponse([property: JsonPropertyName("choices")] FabrykaChoice[] Choices);
    private record FabrykaChoice([property: JsonPropertyName("message")] FabrykaMessage Message);
    private record FabrykaMessage([property: JsonPropertyName("content")] string Content);
}
