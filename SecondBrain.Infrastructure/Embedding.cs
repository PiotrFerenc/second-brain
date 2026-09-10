using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

public class OpenAiEmbedder(IHttpClientFactory httpClientFactory, IOptions<OpenAiOptions> options) : IEmbedder
{
    private readonly OpenAiOptions _options = options.Value;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("OpenAI");
        var response = await client.PostAsJsonAsync("embeddings", new
        {
            model = _options.EmbeddingModel,
            input = text
        }, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<OpenAiEmbeddingResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Pusta odpowiedz OpenAI embeddings.");

        return body.Data[0].Embedding;
    }

    private record OpenAiEmbeddingResponse([property: JsonPropertyName("data")] OpenAiEmbeddingItem[] Data);
    private record OpenAiEmbeddingItem([property: JsonPropertyName("embedding")] float[] Embedding);
}

// ponytail: zablokowany dostep do modelu embeddingow po stronie OpenAI (403 model_not_found
// mimo widocznego dostepu w /v1/models) - mock deterministyczny (ten sam tekst = ten sam
// wektor) zeby pipeline Qdrant dalo sie testowac bez tej zaleznosci. Podmien rejestracje
// w DI z powrotem na OpenAiEmbedder, gdy klucz zacznie dzialac.
public class MockEmbedder(IOptions<QdrantOptions> qdrantOptions) : IEmbedder
{
    private readonly int _size = (int)qdrantOptions.Value.VectorSize;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var seed = BitConverter.ToInt32(MD5.HashData(Encoding.UTF8.GetBytes(text)), 0);
        var random = new Random(seed);

        var vector = new float[_size];
        for (var i = 0; i < _size; i++)
            vector[i] = (float)(random.NextDouble() * 2 - 1);

        return Task.FromResult(vector);
    }
}
