using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

public class FabrykaCompressor(IHttpClientFactory httpClientFactory, IOptions<CompressionOptions> options) : ICompressor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly CompressionOptions _options = options.Value;

    public async Task<CompressionResult> CompressAsync(string rawText, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("Compression");
        var response = await client.PostAsJsonAsync("chat/completions", new
        {
            model = _options.Model,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = _options.SystemPrompt },
                new { role = "user", content = rawText }
            }
        }, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<FabrykaChatResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Pusta odpowiedz Fabryka chat/completions.");

        var raw = body.Choices[0].Message.Content.Trim();
        return JsonSerializer.Deserialize<CompressionResult>(raw, JsonOptions)
            ?? throw new InvalidOperationException($"Nie udalo sie sparsowac JSON z kompresji: {raw}");
    }

    private record FabrykaChatResponse([property: JsonPropertyName("choices")] FabrykaChoice[] Choices);
    private record FabrykaChoice([property: JsonPropertyName("message")] FabrykaMessage Message);
    private record FabrykaMessage([property: JsonPropertyName("content")] string Content);
}
