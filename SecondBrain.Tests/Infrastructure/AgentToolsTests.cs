using System.Text.Json;
using SecondBrain.Core;
using SecondBrain.Infrastructure.AgentTools;
using SecondBrain.Tests.Support;

namespace SecondBrain.Tests.Infrastructure;

public class AgentToolsTests : IDisposable
{
    private readonly Stack _s = new();

    public void Dispose() => _s.Dispose();

    private async Task<Note> AddAsync(string folder, string text, Guid? parent = null)
    {
        await _s.Pipeline.CreateFolderAsync(folder);
        return (await _s.Pipeline.AddAsync(folder, text, parent)).Note;
    }

    // --- foldery ---

    [Fact]
    public async Task ListFolders_ReturnsJsonArray()
    {
        await _s.Pipeline.CreateFolderAsync("A");
        await _s.Pipeline.CreateFolderAsync("B");

        var json = await new ListFoldersTool(_s.Index).ExecuteAsync(Sample.Args("{}"));

        Assert.Equal(["A", "B"], JsonSerializer.Deserialize<string[]>(json)!.Order());
    }

    [Fact]
    public async Task CreateFolder_ThenAgain_ReportsExisting()
    {
        var tool = new CreateFolderTool(_s.Pipeline);
        Assert.Equal("Utworzono folder 'X'.", await tool.ExecuteAsync(Sample.Args(new { name = "X" })));
        Assert.Equal("Folder 'X' juz istnieje.", await tool.ExecuteAsync(Sample.Args(new { name = "X" })));
    }

    [Fact]
    public async Task DeleteFolder_RemovesIt()
    {
        await AddAsync("X", "a");
        var msg = await new DeleteFolderTool(_s.Pipeline).ExecuteAsync(Sample.Args(new { folder = "X" }));

        Assert.Contains("Usunieto folder 'X'", msg);
        Assert.Empty(await _s.Index.ListFoldersAsync());
    }

    [Fact]
    public async Task BulkCreateFolders_ReportsEach()
    {
        await _s.Pipeline.CreateFolderAsync("A");
        var msg = await new BulkCreateFoldersTool(_s.Pipeline).ExecuteAsync(Sample.Args(new { names = new[] { "A", "B" } }));

        Assert.Equal("Folder 'A' juz istnieje.\nUtworzono folder 'B'.", msg);
    }

    [Fact]
    public async Task BulkDeleteFolders_DeletesAll()
    {
        await _s.Pipeline.CreateFolderAsync("A");
        await _s.Pipeline.CreateFolderAsync("B");
        await new BulkDeleteFoldersTool(_s.Pipeline).ExecuteAsync(Sample.Args(new { folders = new[] { "A", "B" } }));

        Assert.Empty(await _s.Index.ListFoldersAsync());
    }

    [Fact]
    public async Task ExportFolder_Empty()
    {
        var msg = await new ExportFolderTool(_s.Store).ExecuteAsync(Sample.Args(new { folder = "nic" }));
        Assert.Equal("Folder 'nic' jest pusty.", msg);
    }

    [Fact]
    public async Task ExportFolder_ConcatenatesNotesSortedByTitle()
    {
        _s.Compressor.Factory = t => new CompressionResult(t.ToUpperInvariant(), "c", [], []);
        await AddAsync("F", "zzz");
        await AddAsync("F", "aaa");

        var md = await new ExportFolderTool(_s.Store).ExecuteAsync(Sample.Args(new { folder = "F" }));

        Assert.True(md.IndexOf("# AAA", StringComparison.Ordinal) < md.IndexOf("# ZZZ", StringComparison.Ordinal));
        Assert.Contains("aaa", md);
        Assert.Contains("---", md);
    }

    // --- notatki: odczyt ---

    [Fact]
    public async Task ListNotes_ShowsIdTitleTags()
    {
        var n = await AddAsync("F", "tresc");
        var json = await new ListNotesTool(_s.Store).ExecuteAsync(Sample.Args(new { folder = "F" }));

        using var doc = JsonDocument.Parse(json);
        var item = doc.RootElement[0];
        Assert.Equal(n.Id, item.GetProperty("Id").GetGuid());
        Assert.Equal("T:tresc", item.GetProperty("Title").GetString());
        Assert.False(item.GetProperty("Pinned").GetBoolean());
    }

    [Fact]
    public async Task GetNote_ReturnsCompressedContent()
    {
        var n = await AddAsync("F", "tresc");
        var json = await new GetNoteTool(_s.Store).ExecuteAsync(Sample.Args(new { folder = "F", noteId = n.Id.ToString() }));

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("C:tresc", doc.RootElement.GetProperty("CompressedContent").GetString());
    }

    [Fact]
    public async Task GetNote_Unknown_Message()
    {
        var id = Guid.NewGuid().ToString();
        var msg = await new GetNoteTool(_s.Store).ExecuteAsync(Sample.Args(new { folder = "F", noteId = id }));
        Assert.Equal($"Nie znaleziono notatki {id}.", msg);
    }

    [Fact]
    public async Task GetNoteTree_NestsChildren()
    {
        var parent = await AddAsync("F", "rodzic");
        var child = await AddAsync("F", "dziecko", parent.Id);

        var json = await new GetNoteTreeTool(_s.Store).ExecuteAsync(Sample.Args(new { folder = "F" }));

        using var doc = JsonDocument.Parse(json);
        var root = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.Equal(parent.Id, root.GetProperty("Id").GetGuid());
        Assert.Equal(child.Id, root.GetProperty("Children")[0].GetProperty("Id").GetGuid());
    }

    [Fact]
    public async Task ListByTag_MatchesCaseInsensitive_AcrossFolders()
    {
        _s.Compressor.Factory = t => new CompressionResult(t, "c", [t.StartsWith('x') ? "Wazne" : "inne"], []);
        await AddAsync("A", "x1");
        await AddAsync("B", "x2");
        await AddAsync("B", "y1");

        var json = await new ListByTagTool(_s.Store, _s.Index).ExecuteAsync(Sample.Args(new { tag = "wazne" }));

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ListByTag_FolderFilter()
    {
        _s.Compressor.Factory = t => new CompressionResult(t, "c", ["t"], []);
        await AddAsync("A", "1");
        await AddAsync("B", "2");

        var json = await new ListByTagTool(_s.Store, _s.Index).ExecuteAsync(Sample.Args(new { tag = "t", folder = "A" }));

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("A", Assert.Single(doc.RootElement.EnumerateArray()).GetProperty("Folder").GetString());
    }

    // --- notatki: zapis ---

    [Fact]
    public async Task AddNote_SavesAndReportsTitle()
    {
        await _s.Pipeline.CreateFolderAsync("F");
        var msg = await new AddNoteTool(_s.Pipeline, _s.Store).ExecuteAsync(Sample.Args(new { folder = "F", text = "nowa" }));

        Assert.Equal("Zapisano notatke 'T:nowa' w folderze 'F'.", msg);
        Assert.Single(await _s.Store.ListAsync("F"));
    }

    [Fact]
    public async Task AddNote_UnknownParent_Refuses()
    {
        await _s.Pipeline.CreateFolderAsync("F");
        var pid = Guid.NewGuid();

        var msg = await new AddNoteTool(_s.Pipeline, _s.Store).ExecuteAsync(Sample.Args(new { folder = "F", text = "x", parentId = pid.ToString() }));

        Assert.Contains("Nie znaleziono notatki-rodzica", msg);
        Assert.Empty(await _s.Store.ListAsync("F"));
    }

    [Fact]
    public async Task AddNote_WithParent_Nests()
    {
        var parent = await AddAsync("F", "rodzic");
        await new AddNoteTool(_s.Pipeline, _s.Store).ExecuteAsync(Sample.Args(new { folder = "F", text = "dziecko", parentId = parent.Id.ToString() }));

        Assert.Single(await _s.Store.ListAsync("F"), n => n.ParentId == parent.Id);
    }

    [Fact]
    public async Task EditNote_UpdatesContent()
    {
        var n = await AddAsync("F", "stara");
        var msg = await new EditNoteTool(_s.Pipeline, _s.Store).ExecuteAsync(Sample.Args(new { folder = "F", noteId = n.Id.ToString(), text = "nowa" }));

        Assert.Equal("Zaktualizowano notatke: T:nowa", msg);
        Assert.Equal("nowa", Assert.Single(await _s.Store.ListAsync("F")).RawContent);
    }

    [Fact]
    public async Task EditNote_Unknown_Message()
    {
        await _s.Pipeline.CreateFolderAsync("F");
        var id = Guid.NewGuid();
        var msg = await new EditNoteTool(_s.Pipeline, _s.Store).ExecuteAsync(Sample.Args(new { folder = "F", noteId = id.ToString(), text = "x" }));
        Assert.Equal($"Nie znaleziono notatki {id} w folderze 'F'.", msg);
    }

    [Theory]
    [InlineData(true, "Przypieto: T:a")]
    [InlineData(false, "Odpieto: T:a")]
    public async Task SetNotePinned_TogglesAndReports(bool pinned, string expected)
    {
        var n = await AddAsync("F", "a");
        var msg = await new SetNotePinnedTool(_s.Pipeline, _s.Store).ExecuteAsync(Sample.Args(new { folder = "F", noteId = n.Id.ToString(), pinned }));

        Assert.Equal(expected, msg);
        Assert.Equal(pinned, Assert.Single(await _s.Store.ListAsync("F")).Pinned);
    }

    [Fact]
    public async Task SetNotePinned_Unknown_Message()
    {
        var msg = await new SetNotePinnedTool(_s.Pipeline, _s.Store).ExecuteAsync(Sample.Args(new { folder = "F", noteId = Guid.NewGuid().ToString(), pinned = true }));
        Assert.StartsWith("Nie znaleziono notatki", msg);
    }

    // --- move ---

    private MoveNoteTool Move => new(_s.Pipeline, _s.Store);

    [Fact]
    public async Task MoveNote_NoTargets_AsksForOne()
    {
        var n = await AddAsync("A", "x");
        var msg = await Move.ExecuteAsync(Sample.Args(new { folder = "A", noteId = n.Id.ToString() }));
        Assert.StartsWith("Podaj targetFolder", msg);
    }

    [Fact]
    public async Task MoveNote_ToOtherFolder()
    {
        var n = await AddAsync("A", "x");
        await _s.Pipeline.CreateFolderAsync("B");

        var msg = await Move.ExecuteAsync(Sample.Args(new { folder = "A", noteId = n.Id.ToString(), targetFolder = "B" }));

        Assert.Equal("Przeniesiono 'T:x' do folderu 'B'.", msg);
        Assert.Empty(await _s.Store.ListAsync("A"));
        Assert.Single(await _s.Store.ListAsync("B"));
    }

    [Fact]
    public async Task MoveNote_UnknownNote()
    {
        await _s.Pipeline.CreateFolderAsync("A");
        var msg = await Move.ExecuteAsync(Sample.Args(new { folder = "A", noteId = Guid.NewGuid().ToString(), targetFolder = "B" }));
        Assert.StartsWith("Nie znaleziono notatki", msg);
    }

    [Fact]
    public async Task MoveNote_WithChildren_BetweenFolders_Refused()
    {
        var parent = await AddAsync("A", "rodzic");
        await AddAsync("A", "dziecko", parent.Id);

        var msg = await Move.ExecuteAsync(Sample.Args(new { folder = "A", noteId = parent.Id.ToString(), targetFolder = "B" }));

        Assert.Contains("podnotatki", msg);
        Assert.Equal(2, (await _s.Store.ListAsync("A")).Count);
    }

    [Fact]
    public async Task MoveNote_UnderNewParent()
    {
        var a = await AddAsync("F", "a");
        var b = await AddAsync("F", "b");

        var msg = await Move.ExecuteAsync(Sample.Args(new { folder = "F", noteId = b.Id.ToString(), newParentId = a.Id.ToString() }));

        Assert.StartsWith("Zaktualizowano rodzica", msg);
        Assert.Equal(a.Id, (await _s.Store.ListAsync("F")).Single(n => n.Id == b.Id).ParentId);
    }

    [Fact]
    public async Task MoveNote_ToRoot_ClearsParent()
    {
        var a = await AddAsync("F", "a");
        var b = await AddAsync("F", "b", a.Id);

        await Move.ExecuteAsync(Sample.Args(new { folder = "F", noteId = b.Id.ToString(), newParentId = "root" }));

        Assert.Null((await _s.Store.ListAsync("F")).Single(n => n.Id == b.Id).ParentId);
    }

    [Fact]
    public async Task MoveNote_OwnParent_Refused()
    {
        var a = await AddAsync("F", "a");
        var msg = await Move.ExecuteAsync(Sample.Args(new { folder = "F", noteId = a.Id.ToString(), newParentId = a.Id.ToString() }));
        Assert.Equal("Notatka nie moze byc wlasnym rodzicem.", msg);
    }

    [Fact]
    public async Task MoveNote_UnknownParent_Refused()
    {
        var a = await AddAsync("F", "a");
        var msg = await Move.ExecuteAsync(Sample.Args(new { folder = "F", noteId = a.Id.ToString(), newParentId = Guid.NewGuid().ToString() }));
        Assert.StartsWith("Nie znaleziono notatki-rodzica", msg);
    }

    [Fact]
    public async Task MoveNote_UnderOwnDescendant_Refused()
    {
        var a = await AddAsync("F", "a");
        var b = await AddAsync("F", "b", a.Id);
        var c = await AddAsync("F", "c", b.Id);

        var msg = await Move.ExecuteAsync(Sample.Args(new { folder = "F", noteId = a.Id.ToString(), newParentId = c.Id.ToString() }));

        Assert.Equal("Nie mozna zagniezdzic notatki pod jej wlasnym potomkiem.", msg);
        Assert.Null((await _s.Store.ListAsync("F")).Single(n => n.Id == a.Id).ParentId);
    }

    // --- bulk ---

    [Fact]
    public async Task BulkImport_ImportsNonBlankLines()
    {
        await _s.Pipeline.CreateFolderAsync("F");
        var msg = await new BulkImportTool(_s.Pipeline).ExecuteAsync(Sample.Args(new { folder = "F", lines = new[] { "a", " ", "b" } }));

        Assert.StartsWith("Zaimportowano 2 notatek do 'F'.", msg);
        Assert.Equal(2, (await _s.Store.ListAsync("F")).Count);
    }

    [Fact]
    public async Task BulkImport_AllBlank_Message()
    {
        var msg = await new BulkImportTool(_s.Pipeline).ExecuteAsync(Sample.Args(new { folder = "F", lines = new[] { " " } }));
        Assert.Equal("Brak niepustych notatek do zaimportowania.", msg);
    }

    [Fact]
    public async Task BulkNoteCreate_SplitsOnCommas()
    {
        await _s.Pipeline.CreateFolderAsync("F");
        await new BulkNoteCreateTool(_s.Pipeline).ExecuteAsync(Sample.Args(new { folder = "F", notes = "Kup mleko, Zadzwon do Jana ,, Zaplac czynsz" }));

        Assert.Equal(3, (await _s.Store.ListAsync("F")).Count);
    }

    [Fact]
    public async Task BulkNoteUpdate_UpdatesValid_ReportsInvalid()
    {
        var n = await AddAsync("F", "stara");
        var updates = $"{n.Id}: nowa tresc,brak-dwukropka,nie-guid: x";

        var msg = await new BulkNoteUpdateTool(_s.Pipeline, _s.Store).ExecuteAsync(Sample.Args(new { folder = "F", updates }));

        var lines = msg.Split('\n');
        Assert.Equal("Zaktualizowano notatke: T:nowa tresc", lines[0]);
        Assert.StartsWith("Zly format wpisu", lines[1]);
        Assert.Equal("Niepoprawne id: 'nie-guid'.", lines[2]);
    }

    [Fact]
    public async Task BulkMove_NoTargets_AsksForOne()
    {
        var msg = await new BulkMoveTool(_s.Pipeline, _s.Store).ExecuteAsync(Sample.Args(new { folder = "F", noteIds = Array.Empty<string>() }));
        Assert.StartsWith("Podaj targetFolder", msg);
    }

    [Fact]
    public async Task BulkMove_MovesAll()
    {
        var a = await AddAsync("A", "a");
        var b = await AddAsync("A", "b");
        await _s.Pipeline.CreateFolderAsync("B");

        await new BulkMoveTool(_s.Pipeline, _s.Store).ExecuteAsync(Sample.Args(new { folder = "A", noteIds = new[] { a.Id.ToString(), b.Id.ToString() }, targetFolder = "B" }));

        Assert.Empty(await _s.Store.ListAsync("A"));
        Assert.Equal(2, (await _s.Store.ListAsync("B")).Count);
    }

    [Fact]
    public async Task BulkTag_AddsAndRemovesCaseInsensitive_NoDuplicates()
    {
        _s.Compressor.Factory = t => new CompressionResult(t, "c", ["Stary", "zostaje"], []);
        var n = await AddAsync("F", "a");

        await new BulkTagTool(_s.Pipeline, _s.Store).ExecuteAsync(Sample.Args(new { folder = "F", noteIds = new[] { n.Id.ToString() }, addTags = new[] { "nowy", "ZOSTAJE" }, removeTags = new[] { "stary" } }));

        Assert.Equal(["zostaje", "nowy"], Assert.Single(await _s.Store.ListAsync("F")).Tags);
    }

    [Fact]
    public async Task BulkTag_UnknownNote_Reported()
    {
        await _s.Pipeline.CreateFolderAsync("F");
        var id = Guid.NewGuid();
        var msg = await new BulkTagTool(_s.Pipeline, _s.Store).ExecuteAsync(Sample.Args(new { folder = "F", noteIds = new[] { id.ToString() } }));
        Assert.Equal($"Nie znaleziono notatki {id}.", msg);
    }

    [Fact]
    public async Task BulkPin_PinsAll()
    {
        var a = await AddAsync("F", "a");
        var b = await AddAsync("F", "b");

        var msg = await new BulkPinTool(_s.Pipeline, _s.Store).ExecuteAsync(Sample.Args(new { folder = "F", noteIds = new[] { a.Id.ToString(), b.Id.ToString() }, pinned = true }));

        Assert.All(await _s.Store.ListAsync("F"), n => Assert.True(n.Pinned));
        Assert.Equal(2, msg.Split('\n').Length);
    }

    // --- skille ---

    [Fact]
    public async Task UseSkill_UnknownName()
    {
        var msg = await new UseSkillTool(_s.Store).ExecuteAsync(Sample.Args(new { name = "nie-ma" }));
        Assert.Equal("Nie ma skilla 'nie-ma'.", msg);
    }

    [Fact]
    public async Task UseSkill_And_ListSkills_UseUserSkillsDir()
    {
        var dir = _s.Root.Combine(".skills");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "Moj.md"), "Opis skilla\nKrok 1\nKrok 2");

        var content = await new UseSkillTool(_s.Store).ExecuteAsync(Sample.Args(new { name = "moj" }));
        var list = await new ListSkillsTool(_s.Store).ExecuteAsync(Sample.Args("{}"));

        Assert.Contains("Krok 2", content);
        using var doc = JsonDocument.Parse(list);
        var item = doc.RootElement.EnumerateArray().Single(e => e.GetProperty("Name").GetString() == "Moj");
        Assert.Equal("Opis skilla", item.GetProperty("Description").GetString());
    }
}
