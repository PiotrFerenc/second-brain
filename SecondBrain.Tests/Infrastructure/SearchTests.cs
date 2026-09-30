using Microsoft.Extensions.Options;
using SecondBrain.Core;
using SecondBrain.Infrastructure;
using SecondBrain.Tests.Support;

namespace SecondBrain.Tests.Infrastructure;

public class HybridNoteSearchTests : IDisposable
{
    private readonly Stack _s = new();

    public void Dispose() => _s.Dispose();

    private async Task<Note> SeedAsync(string folder, string title, string compressed, float[] vector)
    {
        await _s.Index.CreateFolderAsync(folder);
        var note = Sample.Note(title: title, raw: compressed, compressed: compressed);
        var path = await _s.Store.SaveAsync(folder, note);
        note = note with { FilePath = path };
        await _s.Index.UpsertAsync(folder, note, vector);
        return note;
    }

    [Fact]
    public async Task VectorHits_AreIncluded()
    {
        var n = await SeedAsync("F", "kot", "kot spi", [1f, 0f]);
        var r = await HybridNoteSearch.SearchFoldersAsync(_s.Index, _s.Store, ["F"], "cokolwiek", [1f, 0f], 5);

        Assert.Equal(n.Id, Assert.Single(r).Scored.Note.Id);
    }

    [Fact]
    public async Task LexicalMatch_FindsNoteMissedByVectorLimit()
    {
        for (var i = 0; i < 3; i++)
            await SeedAsync("F", $"szum{i}", "nic", [1f, 0f]);
        var target = await SeedAsync("F", "cel", "unikalnaFraza tutaj", [0f, 1f]);

        var r = await HybridNoteSearch.SearchFoldersAsync(_s.Index, _s.Store, ["F"], "unikalnafraza", [1f, 0f], vectorLimit: 1);

        var lexical = r.Single(c => c.Scored.Note.Id == target.Id);
        Assert.Equal(0f, lexical.Scored.Score);
    }

    [Fact]
    public async Task NoDuplicates_WhenFoundByBothPaths()
    {
        var n = await SeedAsync("F", "kot", "kot spi", [1f, 0f]);
        var r = await HybridNoteSearch.SearchFoldersAsync(_s.Index, _s.Store, ["F"], "kot", [1f, 0f], 5);

        Assert.Single(r, c => c.Scored.Note.Id == n.Id);
    }

    [Fact]
    public async Task BlankQuery_SkipsLexicalScan()
    {
        await SeedAsync("F", "a", "tresc", [1f, 0f]);
        await SeedAsync("F", "b", "inna", [0f, 1f]);

        var r = await HybridNoteSearch.SearchFoldersAsync(_s.Index, _s.Store, ["F"], "   ", [1f, 0f], vectorLimit: 1);

        Assert.Single(r);
    }

    [Fact]
    public async Task LexicalMatch_MatchesTitleToo_CaseInsensitive()
    {
        await SeedAsync("F", "Zażółć", "cos innego", [0f, 1f]);
        await SeedAsync("F", "szum", "nic", [1f, 0f]);

        var r = await HybridNoteSearch.SearchFoldersAsync(_s.Index, _s.Store, ["F"], "ZAŻÓŁĆ", [1f, 0f], vectorLimit: 1);

        Assert.Contains(r, c => c.Scored.Note.Title == "Zażółć");
    }

    [Fact]
    public async Task MultipleFolders_TagCandidatesWithFolder()
    {
        await SeedAsync("A", "a", "x", [1f, 0f]);
        await SeedAsync("B", "b", "x", [1f, 0f]);

        var r = await HybridNoteSearch.SearchFoldersAsync(_s.Index, _s.Store, ["A", "B"], "x", [1f, 0f], 5);

        Assert.Equal(["A", "B"], r.Select(c => c.Folder).Order());
    }

    [Fact]
    public async Task NoFolders_NoCandidates() =>
        Assert.Empty(await HybridNoteSearch.SearchFoldersAsync(_s.Index, _s.Store, [], "x", [1f], 5));
}

public class NoteSearchTests : IDisposable
{
    private readonly Stack _s = new();

    public void Dispose() => _s.Dispose();

    [Fact]
    public async Task NullFolders_SearchesAllIndexedFolders()
    {
        await _s.Pipeline.CreateFolderAsync("A");
        await _s.Pipeline.CreateFolderAsync("B");
        await _s.Pipeline.AddAsync("A", "alfa");
        await _s.Pipeline.AddAsync("B", "beta");

        var hits = await _s.Search.SearchAsync("alfa beta");

        Assert.Equal(["A", "B"], hits.Select(h => h.Folder).Order());
    }

    [Fact]
    public async Task ExplicitFolders_LimitScope()
    {
        await _s.Pipeline.CreateFolderAsync("A");
        await _s.Pipeline.CreateFolderAsync("B");
        await _s.Pipeline.AddAsync("A", "wspolne");
        await _s.Pipeline.AddAsync("B", "wspolne");

        var hits = await _s.Search.SearchAsync("wspolne", ["B"]);

        Assert.All(hits, h => Assert.Equal("B", h.Folder));
        Assert.Single(hits);
    }

    [Fact]
    public async Task Hits_ReloadFullNoteFromDisk()
    {
        await _s.Pipeline.CreateFolderAsync("A");
        var added = await _s.Pipeline.AddAsync("A", "tresc");
        // Indeks trzyma CompressedContent w polu RawContent; pelna notatka ma prawdziwy RawContent.
        var hit = Assert.Single(await _s.Search.SearchAsync("tresc", ["A"]));

        Assert.Equal("tresc", hit.Note.RawContent);
        Assert.Equal(added.Note.Id, hit.Note.Id);
    }

    [Fact]
    public async Task StaleIndexEntry_WithMissingFile_KeepsIndexPayload()
    {
        await _s.Pipeline.CreateFolderAsync("A");
        var added = await _s.Pipeline.AddAsync("A", "tresc");
        File.Delete(added.Note.FilePath);

        var hit = Assert.Single(await _s.Search.SearchAsync("tresc", ["A"]));

        Assert.Equal(added.Note.Id, hit.Note.Id);
    }

    [Fact]
    public async Task Empty_NoHits() =>
        Assert.Empty(await _s.Search.SearchAsync("nic"));
}

public class MockRerankerTests
{
    private static ScoredNote Scored(string compressed) => new(Sample.Note(compressed: compressed), 0f);

    [Fact]
    public async Task ScoresByQueryWordOverlap_AndSortsDescending()
    {
        var reranker = new MockReranker();
        var result = await reranker.RerankAsync("kot pies", [Scored("nic"), Scored("kot"), Scored("Kot i PIES")]);

        Assert.Equal([2f, 1f, 0f], result.Select(r => r.Score));
        Assert.Equal("Kot i PIES", result[0].Note.CompressedContent);
    }

    [Fact]
    public async Task EmptyCandidates_Empty() =>
        Assert.Empty(await new MockReranker().RerankAsync("x", []));

    [Fact]
    public async Task EmptyQuery_AllScoresZero()
    {
        var result = await new MockReranker().RerankAsync("", [Scored("a"), Scored("b")]);
        Assert.All(result, r => Assert.Equal(0f, r.Score));
    }
}

public class CohereRerankerTests
{
    private static CohereReranker Make(StubHttpHandler h) =>
        new(new StubHttpClientFactory(h), Options.Create(new RerankerOptions { Model = "rerank-x" }));

    [Fact]
    public async Task Empty_SkipsHttp()
    {
        var h = StubHttpHandler.Json("{}");
        var result = await Make(h).RerankAsync("q", []);

        Assert.Empty(result);
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task MapsIndicesToCandidates_SortedByRelevance()
    {
        var h = StubHttpHandler.Json("""{"results":[{"index":0,"relevance_score":0.1},{"index":2,"relevance_score":0.9},{"index":1,"relevance_score":0.5}]}""");
        var a = new ScoredNote(Sample.Note(title: "a"), 0);
        var b = new ScoredNote(Sample.Note(title: "b"), 0);
        var c = new ScoredNote(Sample.Note(title: "c"), 0);

        var result = await Make(h).RerankAsync("q", [a, b, c]);

        Assert.Equal(["c", "b", "a"], result.Select(r => r.Note.Title));
        Assert.Equal(0.9f, result[0].Score, 3);
    }

    [Fact]
    public async Task SendsModelQueryDocumentsAndTopN()
    {
        var h = StubHttpHandler.Json("""{"results":[{"index":0,"relevance_score":1}]}""");
        await Make(h).RerankAsync("zapytanie", [new ScoredNote(Sample.Note(compressed: "dok"), 0)]);

        using var body = h.LastBody;
        Assert.Equal("rerank-x", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("zapytanie", body.RootElement.GetProperty("query").GetString());
        Assert.Equal("dok", body.RootElement.GetProperty("documents")[0].GetString());
        Assert.Equal(1, body.RootElement.GetProperty("top_n").GetInt32());
        Assert.EndsWith("rerank", h.Requests[0].Request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task HttpError_Throws()
    {
        var h = StubHttpHandler.Json("{}", System.Net.HttpStatusCode.InternalServerError);
        await Assert.ThrowsAsync<HttpRequestException>(() => Make(h).RerankAsync("q", [new ScoredNote(Sample.Note(), 0)]));
    }
}

public class MockEmbedderTests
{
    private static MockEmbedder Make(uint size = 8) => new(Options.Create(new VectorIndexOptions { VectorSize = size }));

    [Fact]
    public async Task SameText_SameVector()
    {
        var e = Make();
        Assert.Equal(await e.EmbedAsync("abc"), await e.EmbedAsync("abc"));
    }

    [Fact]
    public async Task DifferentText_DifferentVector()
    {
        var e = Make();
        Assert.NotEqual(await e.EmbedAsync("abc"), await e.EmbedAsync("abd"));
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(8u)]
    [InlineData(1536u)]
    public async Task VectorHasConfiguredSize(uint size) =>
        Assert.Equal((int)size, (await Make(size).EmbedAsync("x")).Length);

    [Fact]
    public async Task ValuesAreWithinMinusOneAndOne() =>
        Assert.All(await Make(64).EmbedAsync("x"), v => Assert.InRange(v, -1f, 1f));
}
