using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

public class LightOnOcrExtractor(IHttpClientFactory httpClientFactory, IOptions<OcrOptions> options) : IOcrExtractor
{
    private readonly OcrOptions _options = options.Value;

    public async Task<string> ExtractTextAsync(byte[] imageBytes, string mimeType, CancellationToken ct = default)
    {
        var dataUri = $"data:{mimeType};base64,{Convert.ToBase64String(imageBytes)}";

        var client = httpClientFactory.CreateClient("Ocr");
        var response = await client.PostAsJsonAsync("chat/completions", new
        {
            model = _options.Model,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = _options.Prompt },
                        new { type = "image_url", image_url = new { url = dataUri } }
                    }
                }
            }
        }, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Pusta odpowiedz z serwera OCR.");

        return body.Choices[0].Message.Content.Trim();
    }

    private record OpenAiChatResponse([property: JsonPropertyName("choices")] OpenAiChoice[] Choices);
    private record OpenAiChoice([property: JsonPropertyName("message")] OpenAiMessage Message);
    private record OpenAiMessage([property: JsonPropertyName("content")] string Content);
}
