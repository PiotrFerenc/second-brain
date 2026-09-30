using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SecondBrain.Infrastructure;

namespace SecondBrain.Plugins.Rewrite;

// System prompt podaje user w edytorze (pole instrukcji), wiec tu tylko model - bez SystemPrompt.
public class NoteRewriteOptions : HttpClientOptions
{
    public string Model { get; set; } = "gpt-3.5-turbo";
}

// Przepisuje surowy tekst notatki wg krotkiej instrukcji wpisanej przez usera w edytorze
// (instrukcja = system prompt, tekst notatki = wiadomosc usera). Zwraca czysty tekst.
public class NoteRewriter(IHttpClientFactory httpClientFactory, IOptions<NoteRewriteOptions> options)
{
    private readonly NoteRewriteOptions _options = options.Value;

    public async Task<string> RewriteAsync(string instruction, string noteText, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("NoteRewrite");
        var response = await client.PostAsJsonAsync("chat/completions", new
        {
            model = _options.Model,
            messages = new object[]
            {
                new { role = "system", content = instruction },
                new { role = "user", content = noteText }
            }
        }, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<FabrykaChatResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Pusta odpowiedz Fabryka chat/completions.");

        return body.Choices[0].Message.Content.Trim();
    }

    private record FabrykaChatResponse([property: JsonPropertyName("choices")] FabrykaChoice[] Choices);
    private record FabrykaChoice([property: JsonPropertyName("message")] FabrykaMessage Message);
    private record FabrykaMessage([property: JsonPropertyName("content")] string Content);
}
