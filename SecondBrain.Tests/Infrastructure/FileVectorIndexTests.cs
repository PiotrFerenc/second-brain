using SecondBrain.Infrastructure;
using SecondBrain.Tests.Support;

namespace SecondBrain.Tests.Infrastructure;

public class FileVectorIndexTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly FileVectorIndex _index;

    // Upsert zaklada, ze katalog .vectors juz istnieje (tworzy go CreateFolder) - patrz test na koncu.
    public FileVectorIndexTests()
    {
        _index = new FileVectorIndex(_root.Notes);
        Directory.CreateDirectory(_root.Combine(".vectors"));
    }

    public void Dispose() => _root.Dispose();

    [Fact]
    public async Task ListFolders_NoFiles_Empty() =>
        Assert.Empty(await _index.ListFoldersAsync());

    [Fact]
    public async Task ListFolders_MissingDir_Empty()
    {
        using var other = new TempRoot();
        Assert.Empty(await new FileVectorIndex(other.Notes).ListFoldersAsync());
    }

    [Fact]
    public async Task CreateFolder_ReturnsTrueOnce_ThenFalse()
    {
        Assert.True(await _index.CreateFolderAsync("A"));
        Assert.False(await _index.CreateFolderAsync("A"));
        Assert.Equal(["A"], await _index.ListFoldersAsync());
    }

    [Fact]
    public async Task DeleteFolder_RemovesIt()
    {
        await _index.CreateFolderAsync("A");
        await _index.DeleteFolderAsync("A");
        Assert.Empty(await _index.ListFoldersAsync());
    }

    [Fact]
    public async Task DeleteFolder_Missing_DoesNotThrow() =>
        await _index.DeleteFolderAsync("nie-ma");

    [Fact]
    public async Task Search_MissingFolder_Empty() =>
        Assert.Empty(await _index.SearchAsync("nie-ma", [1f, 0f], 5));

    [Fact]
    public async Task Search_OrdersByCosineSimilarity()
    {
        var close = Sample.Note(title: "bliska");
        var far = Sample.Note(title: "daleka");
        var mid = Sample.Note(title: "srednia");
        await _index.UpsertAsync("A", far, [0f, 1f]);
        await _index.UpsertAsync("A", close, [1f, 0.1f]);
        await _index.UpsertAsync("A", mid, [1f, 1f]);

        var hits = await _index.SearchAsync("A", [1f, 0f], 10);

        Assert.Equal(["bliska", "srednia", "daleka"], hits.Select(h => h.Note.Title));
        Assert.True(hits[0].Score > hits[1].Score && hits[1].Score > hits[2].Score);
    }

    [Fact]
    public async Task Search_RespectsLimit()
    {
        for (var i = 0; i < 5; i++)
            await _index.UpsertAsync("A", Sample.Note(title: $"n{i}"), [1f, i]);

        Assert.Equal(2, (await _index.SearchAsync("A", [1f, 0f], 2)).Count);
    }

    [Fact]
    public async Task Search_IdenticalVectors_ScoreOne()
    {
        await _index.UpsertAsync("A", Sample.Note(), [3f, 4f]);
        var hit = Assert.Single(await _index.SearchAsync("A", [3f, 4f], 1));
        Assert.Equal(1f, hit.Score, 4);
    }

    [Fact]
    public async Task Search_OrthogonalVectors_ScoreZero()
    {
        await _index.UpsertAsync("A", Sample.Note(), [1f, 0f]);
        Assert.Equal(0f, Assert.Single(await _index.SearchAsync("A", [0f, 1f], 1)).Score, 4);
    }

    [Fact]
    public async Task Search_ZeroVector_ScoreZeroNotNaN()
    {
        await _index.UpsertAsync("A", Sample.Note(), [0f, 0f]);
        var score = Assert.Single(await _index.SearchAsync("A", [1f, 1f], 1)).Score;
        Assert.Equal(0f, score);
    }

    [Fact]
    public async Task Upsert_SameId_ReplacesEntry()
    {
        var id = Guid.NewGuid();
        await _index.UpsertAsync("A", Sample.Note(title: "stary", id: id), [1f, 0f]);
        await _index.UpsertAsync("A", Sample.Note(title: "nowy", id: id), [1f, 0f]);

        Assert.Equal("nowy", Assert.Single(await _index.SearchAsync("A", [1f, 0f], 10)).Note.Title);
    }

    [Fact]
    public async Task Upsert_CreatesFolderImplicitly_WhenDirExists()
    {
        await _index.CreateFolderAsync("Seed");
        await _index.UpsertAsync("New", Sample.Note(), [1f]);
        Assert.Contains("New", await _index.ListFoldersAsync());
    }

    [Fact]
    public async Task DeleteNote_RemovesOnlyThatOne()
    {
        var keep = Sample.Note(title: "keep");
        var drop = Sample.Note(title: "drop");
        await _index.UpsertAsync("A", keep, [1f, 0f]);
        await _index.UpsertAsync("A", drop, [1f, 0f]);

        await _index.DeleteNoteAsync("A", drop.Id);

        Assert.Equal("keep", Assert.Single(await _index.SearchAsync("A", [1f, 0f], 10)).Note.Title);
    }

    [Fact]
    public async Task Payload_PreservesTagsAndPaths()
    {
        var note = Sample.Note(tags: ["t1", "t2"], filePath: "/x/y.md");
        await _index.UpsertAsync("A", note, [1f]);

        var hit = Assert.Single(await _index.SearchAsync("A", [1f], 1)).Note;
        Assert.Equal(["t1", "t2"], hit.Tags);
        Assert.Equal("/x/y.md", hit.FilePath);
        Assert.Equal(note.CreatedAt, hit.CreatedAt);
    }

    [Fact]
    public async Task ConcurrentUpserts_AreAllStored()
    {
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            _index.UpsertAsync("A", Sample.Note(title: $"n{i}"), [1f, i])));

        Assert.Equal(20, (await _index.SearchAsync("A", [1f, 0f], 100)).Count);
    }

    // Znane ograniczenie: bez wczesniejszego CreateFolder (brak .vectors) zapis wektora rzuca.
    [Fact]
    public async Task Upsert_BeforeAnyCreateFolder_ThrowsDirectoryNotFound()
    {
        using var other = new TempRoot();
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            new FileVectorIndex(other.Notes).UpsertAsync("A", Sample.Note(), [1f]));
    }
}
