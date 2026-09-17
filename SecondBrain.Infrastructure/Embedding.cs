using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

public class FabrykaEmbedder(IHttpClientFactory httpClientFactory, IOptions<EmbeddingOptions> options) : IEmbedder
{
    private readonly EmbeddingOptions _options = options.Value;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("Embedding");
        var response = await client.PostAsJsonAsync("embeddings", new
        {
            model = _options.Model,
            input = text
        }, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<FabrykaEmbeddingResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Pusta odpowiedz Fabryka embeddings.");

        return body.Data[0].Embedding;
    }

    private record FabrykaEmbeddingResponse([property: JsonPropertyName("data")] FabrykaEmbeddingItem[] Data);
    private record FabrykaEmbeddingItem([property: JsonPropertyName("embedding")] float[] Embedding);
}

// ponytail: zablokowany dostep do modelu embeddingow po stronie OpenAI (403 model_not_found
// mimo widocznego dostepu w /v1/models) - mock deterministyczny (ten sam tekst = ten sam
// wektor) zeby pipeline dalo sie testowac bez tej zaleznosci. Podmien rejestracje
// w DI z powrotem na FabrykaEmbedder, gdy klucz zacznie dzialac.
public class MockEmbedder(IOptions<VectorIndexOptions> vectorIndexOptions) : IEmbedder
{
    private readonly int _size = (int)vectorIndexOptions.Value.VectorSize;

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
