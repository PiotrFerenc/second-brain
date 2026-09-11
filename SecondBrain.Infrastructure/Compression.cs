using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

public class OpenAiCompressor(IHttpClientFactory httpClientFactory, IOptions<OpenAiOptions> options) : ICompressor
{
    private const string SystemPrompt =
        "Jestes asystentem kompresujacym notatki uzytkownika do osobistej bazy wiedzy. " +
        "Zwroc WYLACZNIE obiekt JSON o polach: \"title\" (krotki, zwiezly tytul notatki " +
        "ustalony przez Ciebie na podstawie tresci, maks. 80 znakow), \"content\" " +
        "(skompresowana, ustrukturyzowana tresc notatki zachowujaca kluczowe fakty, " +
        "bez powtorzen i dygresji) oraz \"tags\" (2-5 krotkich tagow jednowyrazowych " +
        "po polsku, malymi literami, opisujacych temat notatki).";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly OpenAiOptions _options = options.Value;

    public async Task<CompressionResult> CompressAsync(string rawText, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("OpenAI");
        var response = await client.PostAsJsonAsync("chat/completions", new
        {
            model = _options.CompressionModel,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = rawText }
            }
        }, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Pusta odpowiedz OpenAI chat/completions.");

        var raw = body.Choices[0].Message.Content.Trim();
        return JsonSerializer.Deserialize<CompressionResult>(raw, JsonOptions)
            ?? throw new InvalidOperationException($"Nie udalo sie sparsowac JSON z kompresji: {raw}");
    }

    private record OpenAiChatResponse([property: JsonPropertyName("choices")] OpenAiChoice[] Choices);
    private record OpenAiChoice([property: JsonPropertyName("message")] OpenAiMessage Message);
    private record OpenAiMessage([property: JsonPropertyName("content")] string Content);
}
