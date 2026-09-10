using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

public class OpenAiAnswerSynthesizer(IHttpClientFactory httpClientFactory, IOptions<OpenAiOptions> options) : IAnswerSynthesizer
{
    private const string SystemPrompt =
        "Jestes asystentem odpowiadajacym na pytania wylacznie na podstawie prywatnych " +
        "notatek uzytkownika ponizej. Odpowiadaj zwiezle, po polsku. Jesli notatki nie " +
        "zawieraja odpowiedzi, powiedz to wprost - nie zgaduj i nie dodawaj wiedzy spoza notatek.";

    private readonly OpenAiOptions _options = options.Value;

    public async Task<string> SynthesizeAsync(string query, IReadOnlyList<Note> notes, CancellationToken ct = default)
    {
        var context = string.Join("\n\n---\n\n", notes.Select((n, i) => $"Notatka {i + 1}: {n.Title}\n{n.RawContent}"));
        var userPrompt = $"Notatki:\n{context}\n\nPytanie: {query}";

        var client = httpClientFactory.CreateClient("OpenAI");
        var response = await client.PostAsJsonAsync("chat/completions", new
        {
            model = _options.CompressionModel,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = userPrompt }
            }
        }, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Pusta odpowiedz OpenAI chat/completions.");

        return body.Choices[0].Message.Content.Trim();
    }

    private record OpenAiChatResponse([property: JsonPropertyName("choices")] OpenAiChoice[] Choices);
    private record OpenAiChoice([property: JsonPropertyName("message")] OpenAiMessage Message);
    private record OpenAiMessage([property: JsonPropertyName("content")] string Content);
}
