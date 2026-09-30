using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

public class FabrykaNoteRewriter(IHttpClientFactory httpClientFactory, IOptions<NoteRewriteOptions> options) : INoteRewriter
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
