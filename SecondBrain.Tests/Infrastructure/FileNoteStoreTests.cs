using SecondBrain.Core;
using SecondBrain.Infrastructure;
using SecondBrain.Tests.Support;

namespace SecondBrain.Tests.Infrastructure;

public class FileNoteStoreTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly RecordingEventBus _bus = new();
    private readonly FileNoteStore _store;

    public FileNoteStoreTests() => _store = new FileNoteStore(_root.Notes, _bus);

    public void Dispose() => _root.Dispose();

    [Fact]
    public async Task Save_WritesFileUnderFolderAndYear()
    {
        var note = Sample.Note(created: new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero));
        var path = await _store.SaveAsync("Praca", note);

        Assert.Equal(_root.Combine("Praca", "2025", $"{note.Id}.md"), path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Save_PublishesStorageChanged()
    {
        await _store.SaveAsync("F", Sample.Note(title: "Moja"));
        Assert.Contains("Moja", Assert.Single(_bus.Of<StorageChanged>()).Message);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsAllFields()
    {
        var parent = Guid.NewGuid();
        var note = Sample.Note(title: "Spotkanie: 10:30", raw: "linia 1\nlinia 2\n\nlinia 4", compressed: "## skrot\n- a",
            tags: ["x", "y z"], parentId: parent, pinned: true);
        var path = await _store.SaveAsync("F", note);

        var loaded = await _store.LoadAsync(path);

        Assert.Equal(note.Id, loaded.Id);
        Assert.Equal("Spotkanie: 10:30", loaded.Title);
        Assert.Equal(note.RawContent, loaded.RawContent);
        Assert.Equal(note.CompressedContent, loaded.CompressedContent);
        Assert.Equal(["x", "y z"], loaded.Tags);
        Assert.Equal(parent, loaded.ParentId);
        Assert.True(loaded.Pinned);
        Assert.Equal(note.CreatedAt, loaded.CreatedAt);
        Assert.Equal(note.UpdatedAt, loaded.UpdatedAt);
        Assert.Equal(path, loaded.FilePath);
    }

    [Fact]
    public async Task RoundTrip_NoTags_YieldsEmptyArray()
    {
        var path = await _store.SaveAsync("F", Sample.Note(tags: []));
        Assert.Empty((await _store.LoadAsync(path)).Tags);
    }

    [Fact]
    public async Task RoundTrip_NoParent_YieldsNull()
    {
        var path = await _store.SaveAsync("F", Sample.Note());
        var loaded = await _store.LoadAsync(path);
        Assert.Null(loaded.ParentId);
        Assert.False(loaded.Pinned);
    }

    [Fact]
    public async Task Save_SameNoteTwice_OverwritesSameFile()
    {
        var note = Sample.Note();
        var p1 = await _store.SaveAsync("F", note);
        var p2 = await _store.SaveAsync("F", note with { Title = "zmieniony" });

        Assert.Equal(p1, p2);
        Assert.Single(await _store.ListAsync("F"));
        Assert.Equal("zmieniony", (await _store.LoadAsync(p1)).Title);
    }

    [Fact]
    public async Task Load_WithoutFrontMatter_Throws()
    {
        var path = _root.Combine("bad.md");
        await File.WriteAllTextAsync(path, "brak nagłówka");
        await Assert.ThrowsAsync<FormatException>(() => _store.LoadAsync(path));
    }

    [Fact]
    public async Task Load_EmptyFile_Throws()
    {
        var path = _root.Combine("empty.md");
        await File.WriteAllTextAsync(path, "");
        await Assert.ThrowsAsync<FormatException>(() => _store.LoadAsync(path));
    }

    [Fact]
    public async Task List_UnknownFolder_ReturnsEmpty() =>
        Assert.Empty(await _store.ListAsync("nie-ma"));

    [Fact]
    public async Task List_PinnedFirst_ThenByTitleCaseInsensitive()
    {
        await _store.SaveAsync("F", Sample.Note(title: "banan"));
        await _store.SaveAsync("F", Sample.Note(title: "Ananas"));
        await _store.SaveAsync("F", Sample.Note(title: "cytryna", pinned: true));
        await _store.SaveAsync("F", Sample.Note(title: "Zebra", pinned: true));

        var titles = (await _store.ListAsync("F")).Select(n => n.Title);

        Assert.Equal(["cytryna", "Zebra", "Ananas", "banan"], titles);
    }

    [Fact]
    public async Task List_IncludesAllYears()
    {
        await _store.SaveAsync("F", Sample.Note(title: "a", created: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        await _store.SaveAsync("F", Sample.Note(title: "b", created: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        Assert.Equal(2, (await _store.ListAsync("F")).Count);
    }

    [Fact]
    public async Task List_FoldersAreIsolated()
    {
        await _store.SaveAsync("A", Sample.Note(title: "a"));
        await _store.SaveAsync("B", Sample.Note(title: "b"));

        Assert.Equal("a", Assert.Single(await _store.ListAsync("A")).Title);
    }

    [Fact]
    public async Task Move_ToOtherFolder_RemovesOldFile_AndPublishes()
    {
        var note = Sample.Note();
        var path = await _store.SaveAsync("A", note);
        var newPath = await _store.MoveAsync("A", "B", note with { FilePath = path });

        Assert.False(File.Exists(path));
        Assert.True(File.Exists(newPath));
        Assert.Empty(await _store.ListAsync("A"));
        Assert.Single(await _store.ListAsync("B"));
        Assert.Contains(_bus.Of<StorageChanged>(), e => e.Message.Contains("A -> B"));
    }

    [Fact]
    public async Task Move_WithoutOldFilePath_JustWritesNew()
    {
        var newPath = await _store.MoveAsync("A", "B", Sample.Note(filePath: ""));
        Assert.True(File.Exists(newPath));
    }

    [Fact]
    public async Task DeleteFolder_RemovesDirectoryRecursively()
    {
        await _store.SaveAsync("A", Sample.Note());
        await _store.DeleteFolderAsync("A");

        Assert.False(Directory.Exists(_root.Combine("A")));
    }

    [Fact]
    public async Task DeleteFolder_Missing_StillPublishes()
    {
        await _store.DeleteFolderAsync("nie-ma");
        Assert.Contains(_bus.Of<StorageChanged>(), e => e.Message.Contains("nie-ma"));
    }

    [Fact]
    public async Task Trash_MovesFileToTrashDir_WithFolderPrefix()
    {
        var note = Sample.Note();
        var path = await _store.SaveAsync("Praca", note);

        var trashPath = await _store.MoveToTrashAsync("Praca", path);

        Assert.False(File.Exists(path));
        Assert.Equal(_root.Combine(".trash", $"Praca___{note.Id}.md"), trashPath);
        Assert.True(File.Exists(trashPath));
    }

    [Fact]
    public async Task ListTrash_Empty_WhenNoTrashDir() =>
        Assert.Empty(await _store.ListTrashAsync());

    [Fact]
    public async Task ListTrash_ReturnsOriginalFolder_NewestFirst()
    {
        var older = Sample.Note(title: "starsza") with { UpdatedAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var newer = Sample.Note(title: "nowsza") with { UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        await _store.MoveToTrashAsync("A", await _store.SaveAsync("A", older));
        await _store.MoveToTrashAsync("B", await _store.SaveAsync("B", newer));

        var trash = await _store.ListTrashAsync();

        Assert.Equal(["nowsza", "starsza"], trash.Select(t => t.Note.Title));
        Assert.Equal(["B", "A"], trash.Select(t => t.OriginalFolder));
    }

    [Fact]
    public async Task Restore_MovesBackToOriginalFolder()
    {
        var note = Sample.Note(title: "wroci");
        var trashPath = await _store.MoveToTrashAsync("Praca", await _store.SaveAsync("Praca", note));

        var restored = await _store.RestoreFromTrashAsync(trashPath);

        Assert.Equal("Praca", restored.OriginalFolder);
        Assert.False(File.Exists(trashPath));
        Assert.True(File.Exists(restored.Note.FilePath));
        Assert.Equal("wroci", Assert.Single(await _store.ListAsync("Praca")).Title);
    }

    [Fact]
    public async Task Purge_DeletesFile()
    {
        var trashPath = await _store.MoveToTrashAsync("A", await _store.SaveAsync("A", Sample.Note()));
        await _store.PurgeTrashAsync(trashPath);

        Assert.False(File.Exists(trashPath));
        Assert.Empty(await _store.ListTrashAsync());
    }

    [Fact]
    public async Task Purge_Missing_DoesNotThrow() =>
        await _store.PurgeTrashAsync(_root.Combine(".trash", "nie-ma.md"));

    [Fact]
    public async Task MergeTags_RewritesFilesAcrossFolders_CaseInsensitive()
    {
        await _store.SaveAsync("A", Sample.Note(title: "1", tags: ["Spotkania", "x"]));
        await _store.SaveAsync("B", Sample.Note(title: "2", tags: ["spotkanie"]));
        await _store.SaveAsync("B", Sample.Note(title: "3", tags: ["inne"]));

        var updated = await _store.MergeTagsAsync(["spotkania", "spotkanie"], "spotkanie");

        Assert.Equal(2, updated.Count);
        var a = Assert.Single(await _store.ListAsync("A"));
        Assert.Equal(["x", "spotkanie"], a.Tags);
        var b = (await _store.ListAsync("B")).Single(n => n.Title == "2");
        Assert.Equal(["spotkanie"], b.Tags);
        Assert.Equal(["inne"], (await _store.ListAsync("B")).Single(n => n.Title == "3").Tags);
    }

    [Fact]
    public async Task MergeTags_TargetAlreadyPresent_NoDuplicate()
    {
        await _store.SaveAsync("A", Sample.Note(tags: ["a", "b"]));
        await _store.MergeTagsAsync(["a"], "b");

        Assert.Equal(["b"], Assert.Single(await _store.ListAsync("A")).Tags);
    }

    [Fact]
    public async Task MergeTags_NoMatches_NoPublish()
    {
        await _store.SaveAsync("A", Sample.Note(tags: ["a"]));
        _bus.Events.Clear();

        var updated = await _store.MergeTagsAsync(["zzz"], "b");

        Assert.Empty(updated);
        Assert.Empty(_bus.Events);
    }

    [Fact]
    public async Task MergeTags_SkipsDotFolders()
    {
        var note = Sample.Note(tags: ["a"]);
        var p = await _store.SaveAsync("A", note);
        await _store.MoveToTrashAsync("A", p);
        var updated = await _store.MergeTagsAsync(["a"], "b");

        Assert.Empty(updated);
    }

    [Fact]
    public async Task MergeTags_MissingRoot_ReturnsEmpty()
    {
        using var other = new TempRoot();
        Directory.Delete(other.Path);
        var store = new FileNoteStore(other.Notes, _bus);

        Assert.Empty(await store.MergeTagsAsync(["a"], "b"));
    }

    [Fact]
    public async Task Skills_UserSkillOverridesAndIsSortedByName()
    {
        var dir = _root.Combine(".skills");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "zeta.md"), "opis zeta\nresztka");
        await File.WriteAllTextAsync(Path.Combine(dir, "alfa.md"), "opis alfa");
        await File.WriteAllTextAsync(Path.Combine(dir, "ignored.txt"), "nie");

        var skills = await _store.ListSkillsAsync();

        Assert.Equal(["alfa", "zeta"], skills.Select(s => s.Name));
        Assert.Equal("opis zeta\nresztka", skills.Single(s => s.Name == "zeta").Content);
    }

    [Fact]
    public async Task Skills_None_ReturnsEmpty() =>
        Assert.Empty(await _store.ListSkillsAsync());
}
