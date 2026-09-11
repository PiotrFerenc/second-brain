using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

public class OpenAiAnswerSynthesizer(IHttpClientFactory httpClientFactory, IOptions<OpenAiOptions> options) : IAnswerSynthesizer
{
    private const string SystemPrompt =
        "Jestes asystentem odpowiadajacym na pytania wylacznie na podstawie prywatnych " +
        "notatek uzytkownika ponizej. Zwroc WYLACZNIE obiekt JSON o polach: \"answered\" " +
        "(true jesli notatki faktycznie zawieraja odpowiedz, false jesli nie) oraz \"answer\" " +
        "(zwiezla odpowiedz po polsku gdy answered=true; gdy answered=false, krotkie " +
        "zdanie ze notatki nie zawieraja odpowiedzi - bez zgadywania i bez wiedzy spoza notatek).";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly OpenAiOptions _options = options.Value;

    public async Task<AnswerResult> SynthesizeAsync(string query, IReadOnlyList<Note> notes, CancellationToken ct = default)
    {
        var context = string.Join("\n\n---\n\n", notes.Select((n, i) => $"Notatka {i + 1}: {n.Title}\n{n.RawContent}"));
        var userPrompt = $"Notatki:\n{context}\n\nPytanie: {query}";

        var client = httpClientFactory.CreateClient("OpenAI");
        var response = await client.PostAsJsonAsync("chat/completions", new
        {
            model = _options.CompressionModel,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = userPrompt }
            }
        }, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Pusta odpowiedz OpenAI chat/completions.");

        var raw = body.Choices[0].Message.Content.Trim();
        return JsonSerializer.Deserialize<AnswerResult>(raw, JsonOptions)
            ?? throw new InvalidOperationException($"Nie udalo sie sparsowac JSON z odpowiedzi: {raw}");
    }

    private record OpenAiChatResponse([property: JsonPropertyName("choices")] OpenAiChoice[] Choices);
    private record OpenAiChoice([property: JsonPropertyName("message")] OpenAiMessage Message);
    private record OpenAiMessage([property: JsonPropertyName("content")] string Content);
}
