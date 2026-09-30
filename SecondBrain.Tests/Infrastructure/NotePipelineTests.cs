using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Core;
using SecondBrain.Infrastructure;
using SecondBrain.Tests.Support;

namespace SecondBrain.Tests.Infrastructure;

public class NotePipelineTests : IDisposable
{
    private readonly CaptureHandler<NoteCompressed> _compressed = new();
    private readonly CaptureHandler<NoteAdded> _added = new();
    private readonly CaptureHandler<NoteEdited> _edited = new();
    private readonly CaptureHandler<NoteReindexed> _reindexed = new();
    private readonly CaptureHandler<NoteTrashed> _trashed = new();
    private readonly CaptureHandler<NoteRestored> _restored = new();
    private readonly CaptureHandler<NotePurged> _purged = new();
    private readonly CaptureHandler<FolderCreated> _folderCreated = new();
    private readonly CaptureHandler<FolderDeleted> _folderDeleted = new();
    private readonly CaptureHandler<ImportCompleted> _import = new();
    private readonly CaptureHandler<NotesChanged> _changed = new();
    private readonly Stack _s;

    public NotePipelineTests()
    {
        _s = new Stack(services =>
        {
            services.AddSingleton<IEventHandler<NoteCompressed>>(_compressed);
            services.AddSingleton<IEventHandler<NoteAdded>>(_added);
            services.AddSingleton<IEventHandler<NoteEdited>>(_edited);
            services.AddSingleton<IEventHandler<NoteReindexed>>(_reindexed);
            services.AddSingleton<IEventHandler<NoteTrashed>>(_trashed);
            services.AddSingleton<IEventHandler<NoteRestored>>(_restored);
            services.AddSingleton<IEventHandler<NotePurged>>(_purged);
            services.AddSingleton<IEventHandler<FolderCreated>>(_folderCreated);
            services.AddSingleton<IEventHandler<FolderDeleted>>(_folderDeleted);
            services.AddSingleton<IEventHandler<ImportCompleted>>(_import);
            services.AddSingleton<IEventHandler<NotesChanged>>(_changed);
        });
    }

    public void Dispose() => _s.Dispose();

    private async Task<AddNoteResult> AddAsync(string folder, string text, string[]? tags = null, Guid? parent = null)
    {
        await _s.Pipeline.CreateFolderAsync(folder);
        return await _s.Pipeline.AddAsync(folder, text, parent, tags);
    }

    [Fact]
    public async Task Add_SavesFile_AndIndexesIt()
    {
        var r = await AddAsync("F", "surowy tekst");

        Assert.True(File.Exists(r.Note.FilePath));
        Assert.Equal("T:surowy tekst", r.Note.Title);
        Assert.Equal("surowy tekst", r.Note.RawContent);
        Assert.Equal("C:surowy tekst", r.Note.CompressedContent);
        var hit = Assert.Single(await _s.Index.SearchAsync("F", [1f, 1f, 0f], 10));
        Assert.Equal(r.Note.Id, hit.Note.Id);
    }

    [Fact]
    public async Task Add_EmbedsCompressedContent_NotRaw()
    {
        await AddAsync("F", "raw");
        Assert.Contains("C:raw", _s.Embedder.Inputs);
    }

    [Fact]
    public async Task Add_UsesCompressionTags_WhenNoUserTags()
    {
        _s.Compressor.Factory = t => new CompressionResult("t", "c", ["z-llm"], []);
        var r = await AddAsync("F", "x");

        Assert.Equal(["z-llm"], r.Note.Tags);
        Assert.False(Assert.Single(_compressed.Seen).TagsFromUser);
    }

    [Fact]
    public async Task Add_UserTags_OverrideCompressionTags()
    {
        _s.Compressor.Factory = t => new CompressionResult("t", "c", ["z-llm"], []);
        var r = await AddAsync("F", "x", tags: ["moj"]);

        Assert.Equal(["moj"], r.Note.Tags);
        Assert.True(Assert.Single(_compressed.Seen).TagsFromUser);
    }

    [Fact]
    public async Task Add_HandlerCanChangeTagsBeforeSave()
    {
        var pipelineStack = new Stack(s => s.AddSingleton<IEventHandler<NoteCompressed>>(new CaptureHandler<NoteCompressed>(e => e.Tags.Add("dolozony"))));
        using var _ = pipelineStack;
        await pipelineStack.Pipeline.CreateFolderAsync("F");
        var r = await pipelineStack.Pipeline.AddAsync("F", "x");

        Assert.Contains("dolozony", r.Note.Tags);
        Assert.Contains("dolozony", (await pipelineStack.Store.LoadAsync(r.Note.FilePath)).Tags);
    }

    [Fact]
    public async Task Add_PublishesNoteAdded_WithNoticesAndFlags()
    {
        var r = await AddAsync("F", "x");

        var e = Assert.Single(_added.Seen);
        Assert.Equal("F", e.Folder);
        Assert.Equal(r.Note.Id, e.Note.Id);
        Assert.False(e.FromImport);
        Assert.Empty(e.Notices);
        Assert.Equal(2, _changed.Seen.Count); // CreateFolder + Add
    }

    [Fact]
    public async Task Add_HandlerNotices_AreReturned()
    {
        using var stack = new Stack(s => s.AddSingleton<IEventHandler<NoteAdded>>(new CaptureHandler<NoteAdded>(e => e.Notices.Add("uwaga"))));
        await stack.Pipeline.CreateFolderAsync("F");

        var r = await stack.Pipeline.AddAsync("F", "x");

        Assert.Equal(["uwaga"], r.Notices);
    }

    [Fact]
    public async Task Add_RelatedNotes_ExcludeSelf_AndCapAtThree()
    {
        for (var i = 0; i < 6; i++)
            await AddAsync("F", $"n{i}");

        var last = await _s.Pipeline.AddAsync("F", "ostatnia");

        Assert.True(last.Related.Count <= 3);
        Assert.DoesNotContain(last.Related, n => n.Id == last.Note.Id);
    }

    [Fact]
    public async Task Add_FirstNote_HasNoRelated()
    {
        var r = await AddAsync("F", "pierwsza");
        Assert.Empty(r.Related);
    }

    [Fact]
    public async Task Add_WithParent_StoresParentId()
    {
        var parent = await AddAsync("F", "rodzic");
        var child = await _s.Pipeline.AddAsync("F", "dziecko", parent.Note.Id);

        Assert.Equal(parent.Note.Id, (await _s.Store.LoadAsync(child.Note.FilePath)).ParentId);
    }

    [Fact]
    public async Task Add_CompressorFails_NothingSaved()
    {
        await _s.Pipeline.CreateFolderAsync("F");
        _s.Compressor.Factory = _ => throw new InvalidOperationException("LLM padl");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _s.Pipeline.AddAsync("F", "x"));
        Assert.Empty(await _s.Store.ListAsync("F"));
        Assert.Empty(_added.Seen);
    }

    [Fact]
    public async Task Edit_OverwritesSameFile_AndUpdatesIndex()
    {
        var added = await AddAsync("F", "stary");
        _s.Compressor.Factory = t => new CompressionResult("Nowy tytul", "nowa tresc", ["n"], []);

        var edited = await _s.Pipeline.EditAsync("F", added.Note, "nowy tekst");

        Assert.Equal(added.Note.FilePath, edited.FilePath);
        Assert.Equal(added.Note.CreatedAt, edited.CreatedAt);
        Assert.Equal("Nowy tytul", edited.Title);
        Assert.Equal("nowy tekst", edited.RawContent);
        Assert.Equal(["n"], edited.Tags);
        Assert.True(edited.UpdatedAt >= added.Note.UpdatedAt);
        Assert.Single(await _s.Store.ListAsync("F"));
        var hit = Assert.Single(await _s.Index.SearchAsync("F", [1f, 1f, 0f], 10));
        Assert.Equal("Nowy tytul", hit.Note.Title);
        Assert.Single(_edited.Seen);
    }

    [Fact]
    public async Task Import_AddsEachNonBlankLine_AsImport()
    {
        await _s.Pipeline.CreateFolderAsync("F");
        var r = await _s.Pipeline.ImportAsync("F", ["  a  ", "", "   ", "b"]);

        Assert.Equal(2, r.Count);
        Assert.Equal(2, (await _s.Store.ListAsync("F")).Count);
        Assert.All(_added.Seen, e => Assert.True(e.FromImport));
        Assert.Contains("a", _s.Compressor.Inputs);
    }

    [Fact]
    public async Task Import_PublishesCompletedOnce()
    {
        await _s.Pipeline.CreateFolderAsync("F");
        await _s.Pipeline.ImportAsync("F", ["a", "b", "c"]);

        var e = Assert.Single(_import.Seen);
        Assert.Equal(3, e.Count);
        Assert.Equal("F", e.Folder);
    }

    [Fact]
    public async Task Import_Nothing_PublishesNothing()
    {
        await _s.Pipeline.CreateFolderAsync("F");
        var r = await _s.Pipeline.ImportAsync("F", ["", "  "]);

        Assert.Equal(0, r.Count);
        Assert.Empty(_import.Seen);
    }

    [Fact]
    public async Task Import_CollectsNoticesFromHandlers()
    {
        using var stack = new Stack(s => s.AddSingleton<IEventHandler<ImportCompleted>>(new CaptureHandler<ImportCompleted>(e => e.Notices.Add("zamknieto luke"))));
        await stack.Pipeline.CreateFolderAsync("F");

        var r = await stack.Pipeline.ImportAsync("F", ["a"]);

        Assert.Equal(["zamknieto luke"], r.Notices);
    }

    [Fact]
    public async Task Reindex_DoesNotCompress()
    {
        var added = await AddAsync("F", "x");
        var before = _s.Compressor.Inputs.Count;

        var pinned = await _s.Pipeline.ReindexAsync("F", added.Note with { Pinned = true });

        Assert.Equal(before, _s.Compressor.Inputs.Count);
        Assert.True((await _s.Store.LoadAsync(pinned.FilePath)).Pinned);
        Assert.Single(_reindexed.Seen);
    }

    [Fact]
    public async Task Move_SameFolder_IsReindex()
    {
        var added = await AddAsync("F", "x");
        await _s.Pipeline.MoveAsync("F", "F", added.Note with { Pinned = true });

        Assert.Single(_reindexed.Seen);
        Assert.Single(await _s.Store.ListAsync("F"));
    }

    [Fact]
    public async Task Move_OtherFolder_MovesFileAndIndexEntry()
    {
        var added = await AddAsync("A", "x");
        await _s.Pipeline.CreateFolderAsync("B");

        var moved = await _s.Pipeline.MoveAsync("A", "B", added.Note);

        Assert.False(File.Exists(added.Note.FilePath));
        Assert.True(File.Exists(moved.FilePath));
        Assert.Empty(await _s.Index.SearchAsync("A", [1f, 1f, 0f], 10));
        Assert.Single(await _s.Index.SearchAsync("B", [1f, 1f, 0f], 10));
        Assert.Equal("B", Assert.Single(_reindexed.Seen).Folder);
    }

    [Fact]
    public async Task Trash_RemovesFromIndex_AndFolder()
    {
        var added = await AddAsync("F", "x");
        await _s.Pipeline.TrashAsync("F", added.Note);

        Assert.Empty(await _s.Store.ListAsync("F"));
        Assert.Empty(await _s.Index.SearchAsync("F", [1f, 1f, 0f], 10));
        Assert.Single(await _s.Store.ListTrashAsync());
        Assert.Single(_trashed.Seen);
    }

    [Fact]
    public async Task Restore_ReturnsToFolderAndIndex()
    {
        var added = await AddAsync("F", "x");
        await _s.Pipeline.TrashAsync("F", added.Note);
        var trash = Assert.Single(await _s.Store.ListTrashAsync());

        var restored = await _s.Pipeline.RestoreAsync(trash.TrashPath);

        Assert.Equal("F", restored.OriginalFolder);
        Assert.Single(await _s.Store.ListAsync("F"));
        Assert.Single(await _s.Index.SearchAsync("F", [1f, 1f, 0f], 10));
        Assert.Empty(await _s.Store.ListTrashAsync());
        Assert.Single(_restored.Seen);
    }

    [Fact]
    public async Task Purge_DeletesForever_AndPublishes()
    {
        var added = await AddAsync("F", "x");
        await _s.Pipeline.TrashAsync("F", added.Note);
        var trash = Assert.Single(await _s.Store.ListTrashAsync());

        await _s.Pipeline.PurgeAsync(trash.TrashPath);

        Assert.Empty(await _s.Store.ListTrashAsync());
        Assert.Equal(trash.TrashPath, Assert.Single(_purged.Seen).TrashPath);
    }

    [Fact]
    public async Task CreateFolder_NewThenExisting()
    {
        Assert.True(await _s.Pipeline.CreateFolderAsync("F"));
        Assert.False(await _s.Pipeline.CreateFolderAsync("F"));

        Assert.Single(_folderCreated.Seen);
        Assert.Single(_changed.Seen);
    }

    [Fact]
    public async Task DeleteFolder_RemovesNotesAndIndex()
    {
        await AddAsync("F", "x");
        await _s.Pipeline.DeleteFolderAsync("F");

        Assert.Empty(await _s.Index.ListFoldersAsync());
        Assert.Empty(await _s.Store.ListAsync("F"));
        Assert.Equal("F", Assert.Single(_folderDeleted.Seen).Folder);
    }
}
