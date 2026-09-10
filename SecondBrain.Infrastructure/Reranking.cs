using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

// ponytail: mock na czas testow lokalnych - realny reranker (CohereReranker ponizej albo inny
// provider) wpina sie pod ten sam interfejs i uzywa juz gotowego, nazwanego HttpClienta
// "Reranker" z appsettings.json.
public class MockReranker : IReranker
{
    public Task<IReadOnlyList<ScoredNote>> RerankAsync(string query, IReadOnlyList<ScoredNote> candidates, CancellationToken ct = default)
    {
        var queryWords = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var reranked = candidates
            .Select(c => c with { Score = CountOverlap(c.Note.CompressedContent, queryWords) })
            .OrderByDescending(c => c.Score)
            .ToList();

        return Task.FromResult<IReadOnlyList<ScoredNote>>(reranked);
    }

    private static float CountOverlap(string content, string[] queryWords)
    {
        var contentLower = content.ToLowerInvariant();
        return queryWords.Count(w => contentLower.Contains(w));
    }
}

// Realny reranker pod wire-format Cohere v2/rerank (dziala tez z kazdym API o identycznym
// ksztalcie zapytania/odpowiedzi - endpoint, naglowki i model biora sie z appsettings.json,
// wiec podmiana providera to zmiana konfiguracji, nie kodu).
public class CohereReranker(IHttpClientFactory httpClientFactory, IOptions<RerankerOptions> options) : IReranker
{
    private readonly RerankerOptions _options = options.Value;

    public async Task<IReadOnlyList<ScoredNote>> RerankAsync(string query, IReadOnlyList<ScoredNote> candidates, CancellationToken ct = default)
    {
        if (candidates.Count == 0)
            return candidates;

        var client = httpClientFactory.CreateClient("Reranker");
        var response = await client.PostAsJsonAsync("rerank", new
        {
            model = _options.Model,
            query,
            documents = candidates.Select(c => c.Note.CompressedContent).ToArray(),
            top_n = candidates.Count
        }, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<RerankResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Pusta odpowiedz rerankera.");

        return body.Results
            .OrderByDescending(r => r.RelevanceScore)
            .Select(r => candidates[r.Index] with { Score = r.RelevanceScore })
            .ToList();
    }

    private record RerankResponse([property: JsonPropertyName("results")] RerankResult[] Results);
    private record RerankResult(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("relevance_score")] float RelevanceScore);
}
