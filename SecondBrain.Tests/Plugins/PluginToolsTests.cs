using System.Text.Json;
using Microsoft.Extensions.Options;
using SecondBrain.Core;
using SecondBrain.Infrastructure;
using SecondBrain.Plugins.Conflicts;
using SecondBrain.Plugins.Duplicates;
using SecondBrain.Plugins.Gaps;
using SecondBrain.Plugins.Glossary;
using SecondBrain.Plugins.Search;
using SecondBrain.Plugins.Tags;
using SecondBrain.Plugins.Templates;
using SecondBrain.Plugins.Trash;
using SecondBrain.Tests.Support;

namespace SecondBrain.Tests.Plugins;

public class GapToolsTests : IDisposable
{
    private readonly Stack _s = new();
    private GapStore Gaps => new(_s.Root.Notes, _s.Bus);

    public void Dispose() => _s.Dispose();

    [Fact]
    public async Task ListGaps_ReturnsQueries()
    {
        var gaps = Gaps;
        await gaps.LogAsync("co z kotem?");

        var json = await new ListGapsTool(gaps).ExecuteAsync(Sample.Args("{}"));

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("co z kotem?", doc.RootElement[0].GetProperty("Query").GetString());
    }

    [Fact]
    public async Task ListGaps_Empty_IsEmptyArray() =>
        Assert.Equal("[]", await new ListGapsTool(Gaps).ExecuteAsync(Sample.Args("{}")));

    [Fact]
    public async Task ResolveGap_DeletesIt()
    {
        var gaps = Gaps;
        await gaps.LogAsync("x");
        var path = (await gaps.ListAsync())[0].Path;

        var msg = await new ResolveGapTool(gaps).ExecuteAsync(Sample.Args(new { path }));

        Assert.Equal("Odrzucono luke.", msg);
        Assert.Empty(await gaps.ListAsync());
    }

    [Fact]
    public async Task BulkResolveGaps_DeletesAll_AndCounts()
    {
        var gaps = Gaps;
        await gaps.LogAsync("a");
        await gaps.LogAsync("b");
        var paths = (await gaps.ListAsync()).Select(g => g.Path).ToArray();

        var msg = await new BulkResolveGapsTool(gaps).ExecuteAsync(Sample.Args(new { paths }));

        Assert.Equal("Odrzucono 2 luk w wiedzy.", msg);
        Assert.Empty(await gaps.ListAsync());
    }

    [Fact]
    public void Describe_UsesArguments()
    {
        Assert.Contains("/x.md", new ResolveGapTool(Gaps).Describe(Sample.Args(new { path = "/x.md" })));
        Assert.Contains("3", new BulkResolveGapsTool(Gaps).Describe(Sample.Args(new { paths = new[] { "a", "b", "c" } })));
    }
}

public class GlossaryToolsTests : IDisposable
{
    private readonly TempRoot _root = new();
    private GlossaryStore Store => new(_root.Notes, new RecordingEventBus());

    public void Dispose() => _root.Dispose();

    [Fact]
    public async Task AddThenList()
    {
        var store = Store;
        var msg = await new AddGlossaryEntryTool(store).ExecuteAsync(Sample.Args(new { term = "RAG", definition = "opis" }));

        Assert.Equal("Dodano do slownika: RAG", msg);
        using var doc = JsonDocument.Parse(await new ListGlossaryTool(store).ExecuteAsync(Sample.Args("{}")));
        Assert.Equal("Reczny wpis", doc.RootElement[0].GetProperty("SourceTitle").GetString());
    }

    [Fact]
    public async Task Delete_ExistingAndMissing()
    {
        var store = Store;
        await store.SaveAsync("RAG", "d", "s");
        var tool = new DeleteGlossaryEntryTool(store);

        Assert.Equal("Usunieto ze slownika: RAG", await tool.ExecuteAsync(Sample.Args(new { term = "RAG" })));
        Assert.Equal("Nie znaleziono terminu 'RAG' w slowniku.", await tool.ExecuteAsync(Sample.Args(new { term = "RAG" })));
    }

    [Fact]
    public void Describe_TruncatesLongDefinitions()
    {
        var text = new AddGlossaryEntryTool(Store).Describe(Sample.Args(new { term = "T", definition = new string('x', 500) }));
        Assert.True(text.Length < 200);
        Assert.Contains("...", text);
    }
}

public class TrashToolsTests : IDisposable
{
    private readonly Stack _s = new();

    public void Dispose() => _s.Dispose();

    private async Task<Note> AddAsync(string folder, string text)
    {
        await _s.Pipeline.CreateFolderAsync(folder);
        return (await _s.Pipeline.AddAsync(folder, text)).Note;
    }

    [Fact]
    public async Task TrashNote_MovesToTrash()
    {
        var n = await AddAsync("F", "a");
        var msg = await new TrashNoteTool(_s.Pipeline, _s.Store).ExecuteAsync(Sample.Args(new { folder = "F", noteId = n.Id.ToString() }));

        Assert.Equal("Przeniesiono do kosza: T:a", msg);
        Assert.Empty(await _s.Store.ListAsync("F"));
        Assert.Single(await _s.Store.ListTrashAsync());
    }

    [Fact]
    public async Task TrashNote_Unknown()
    {
        await _s.Pipeline.CreateFolderAsync("F");
        var id = Guid.NewGuid();
        var msg = await new TrashNoteTool(_s.Pipeline, _s.Store).ExecuteAsync(Sample.Args(new { folder = "F", noteId = id.ToString() }));
        Assert.Equal($"Nie znaleziono notatki {id} w folderze 'F'.", msg);
    }

    [Fact]
    public async Task ListTrash_ShowsTitleAndFolder()
    {
        var n = await AddAsync("F", "a");
        await _s.Pipeline.TrashAsync("F", n);

        using var doc = JsonDocument.Parse(await new ListTrashTool(_s.Store).ExecuteAsync(Sample.Args("{}")));
        var item = doc.RootElement[0];
        Assert.Equal("F", item.GetProperty("OriginalFolder").GetString());
        Assert.Equal("T:a", item.GetProperty("Title").GetString());
    }

    [Fact]
    public async Task RestoreNote_BringsItBack()
    {
        var n = await AddAsync("F", "a");
        await _s.Pipeline.TrashAsync("F", n);
        var path = (await _s.Store.ListTrashAsync())[0].TrashPath;

        var msg = await new RestoreNoteTool(_s.Pipeline).ExecuteAsync(Sample.Args(new { trashPath = path }));

        Assert.Equal("Przywrocono 'T:a' do folderu 'F'.", msg);
        Assert.Single(await _s.Store.ListAsync("F"));
    }

    [Fact]
    public async Task PurgeNote_RemovesFromTrash()
    {
        var n = await AddAsync("F", "a");
        await _s.Pipeline.TrashAsync("F", n);
        var path = (await _s.Store.ListTrashAsync())[0].TrashPath;

        Assert.Equal("Usunieto trwale.", await new PurgeNoteTool(_s.Pipeline).ExecuteAsync(Sample.Args(new { trashPath = path })));
        Assert.Empty(await _s.Store.ListTrashAsync());
    }

    [Fact]
    public async Task PurgeAll_EmptiesTrash_AndCounts()
    {
        foreach (var t in new[] { "a", "b", "c" })
            await _s.Pipeline.TrashAsync("F", await AddAsync("F", t));

        var msg = await new PurgeTrashAllTool(_s.Pipeline, _s.Store).ExecuteAsync(Sample.Args("{}"));

        Assert.Equal("Trwale usunieto 3 notatek z kosza.", msg);
        Assert.Empty(await _s.Store.ListTrashAsync());
    }

    [Fact]
    public async Task PurgeAll_EmptyTrash_ReportsZero() =>
        Assert.Equal("Trwale usunieto 0 notatek z kosza.", await new PurgeTrashAllTool(_s.Pipeline, _s.Store).ExecuteAsync(Sample.Args("{}")));

    [Fact]
    public async Task BulkRestore_RestoresAll()
    {
        await _s.Pipeline.TrashAsync("F", await AddAsync("F", "a"));
        await _s.Pipeline.TrashAsync("F", await AddAsync("F", "b"));
        var paths = (await _s.Store.ListTrashAsync()).Select(t => t.TrashPath).ToArray();

        var msg = await new BulkRestoreTool(_s.Pipeline).ExecuteAsync(Sample.Args(new { trashPaths = paths }));

        Assert.Equal(2, msg.Split('\n').Length);
        Assert.Equal(2, (await _s.Store.ListAsync("F")).Count);
    }

    [Fact]
    public async Task BulkPurge_RemovesOnlyListed()
    {
        await _s.Pipeline.TrashAsync("F", await AddAsync("F", "a"));
        await _s.Pipeline.TrashAsync("F", await AddAsync("F", "b"));
        var all = await _s.Store.ListTrashAsync();

        var msg = await new BulkPurgeTool(_s.Pipeline).ExecuteAsync(Sample.Args(new { trashPaths = new[] { all[0].TrashPath } }));

        Assert.Equal("Trwale usunieto 1 notatek z kosza.", msg);
        Assert.Single(await _s.Store.ListTrashAsync());
    }

    [Fact]
    public async Task BulkTrash_TrashesFound_ReportsMissing()
    {
        var a = await AddAsync("F", "a");
        var missing = Guid.NewGuid();

        var msg = await new BulkTrashTool(_s.Pipeline, _s.Store).ExecuteAsync(Sample.Args(new { folder = "F", noteIds = new[] { a.Id.ToString(), missing.ToString() } }));

        Assert.Equal($"Do kosza: T:a\nNie znaleziono notatki {missing}.", msg);
        Assert.Empty(await _s.Store.ListAsync("F"));
    }
}

public class SearchToolsTests : IDisposable
{
    private readonly Stack _s = new();

    public void Dispose() => _s.Dispose();

    private async Task SeedAsync(string folder, string text)
    {
        await _s.Pipeline.CreateFolderAsync(folder);
        await _s.Pipeline.AddAsync(folder, text);
    }

    [Fact]
    public async Task SearchNotes_ReturnsHitsWithFolderAndSnippet()
    {
        await SeedAsync("F", "kot spi");
        var json = await new SearchNotesTool(_s.Search).ExecuteAsync(Sample.Args(new { query = "kot" }));

        using var doc = JsonDocument.Parse(json);
        var hit = doc.RootElement[0];
        Assert.Equal("F", hit.GetProperty("Folder").GetString());
        Assert.Equal("C:kot spi", hit.GetProperty("Snippet").GetString());
    }

    [Fact]
    public async Task SearchNotes_LimitsToTenAndTruncatesSnippet()
    {
        _s.Compressor.Factory = t => new CompressionResult(t, new string('z', 500), [], []);
        for (var i = 0; i < 12; i++)
            await SeedAsync("F", $"n{i}");

        using var doc = JsonDocument.Parse(await new SearchNotesTool(_s.Search).ExecuteAsync(Sample.Args(new { query = "n" })));

        Assert.Equal(10, doc.RootElement.GetArrayLength());
        Assert.Equal(203, doc.RootElement[0].GetProperty("Snippet").GetString()!.Length);
    }

    [Fact]
    public async Task SearchNotes_FolderFilter()
    {
        await SeedAsync("A", "wspolne");
        await SeedAsync("B", "wspolne");

        using var doc = JsonDocument.Parse(await new SearchNotesTool(_s.Search).ExecuteAsync(Sample.Args(new { query = "wspolne", folder = "A" })));

        Assert.Equal("A", Assert.Single(doc.RootElement.EnumerateArray()).GetProperty("Folder").GetString());
    }

    [Fact]
    public async Task AskQuestion_NoNotes_SaysSo_AndPublishesNothing()
    {
        var bus = new RecordingEventBus();
        var tool = new AskQuestionTool(_s.Search, _s.Synthesizer, bus);

        var msg = await tool.ExecuteAsync(Sample.Args(new { query = "cokolwiek" }));

        Assert.Equal("Brak notatek pasujacych do tego pytania.", msg);
        Assert.Empty(bus.Events);
        Assert.Empty(_s.Synthesizer.Calls);
    }

    [Fact]
    public async Task AskQuestion_ReturnsAnswer_AndPublishesSearchCompleted()
    {
        await SeedAsync("F", "kot ma 5 lat");
        var bus = new RecordingEventBus();
        var synth = new FakeSynthesizer((_, _) => new AnswerResult(true, "5 lat"));

        var msg = await new AskQuestionTool(_s.Search, synth, bus).ExecuteAsync(Sample.Args(new { query = "kot" }));

        Assert.Equal("5 lat", msg);
        var e = Assert.Single(bus.Of<SearchCompleted>());
        Assert.Equal("kot", e.Query);
        Assert.True(e.Answered);
    }

    [Fact]
    public async Task AskQuestion_NotAnswered_PublishesAnsweredFalse()
    {
        await SeedAsync("F", "kot");
        var bus = new RecordingEventBus();
        var synth = new FakeSynthesizer((_, _) => new AnswerResult(false, "brak danych"));

        await new AskQuestionTool(_s.Search, synth, bus).ExecuteAsync(Sample.Args(new { query = "kot" }));

        Assert.False(Assert.Single(bus.Of<SearchCompleted>()).Answered);
    }

    [Fact]
    public async Task AskQuestion_PassesAtMostFiveNotesToSynthesizer()
    {
        for (var i = 0; i < 8; i++)
            await SeedAsync("F", $"kot{i}");
        var synth = new FakeSynthesizer();

        await new AskQuestionTool(_s.Search, synth, new RecordingEventBus()).ExecuteAsync(Sample.Args(new { query = "kot" }));

        Assert.Equal(5, Assert.Single(synth.Calls).Notes.Count);
    }
}

public class TagToolsTests : IDisposable
{
    private readonly Stack _s = new();

    public void Dispose() => _s.Dispose();

    [Fact]
    public async Task FindDuplicateTags_SendsDistinctTagsFromAllFolders()
    {
        _s.Compressor.Factory = t => new CompressionResult(t, "c", t == "1" ? ["Spotkanie", "kot"] : ["spotkanie", "pies"], []);
        foreach (var (folder, text) in new[] { ("A", "1"), ("B", "2") })
        {
            await _s.Pipeline.CreateFolderAsync(folder);
            await _s.Pipeline.AddAsync(folder, text);
        }
        var http = StubHttpHandler.Chat("""{"groups":[{"tags":["Spotkanie","spotkanie"],"suggestedCanonical":"spotkanie"}]}""");
        var cleaner = new TagCleaner(new StubHttpClientFactory(http), Options.Create(new TagCleaningOptions()));

        var json = await new FindDuplicateTagsTool(_s.Store, _s.Index, cleaner).ExecuteAsync(Sample.Args("{}"));

        using var sent = http.LastBody;
        var tags = sent.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!.Split(", ");
        Assert.Equal(3, tags.Length);   // Spotkanie/spotkanie sprowadzone do jednego (case-insensitive)
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("spotkanie", doc.RootElement[0].GetProperty("SuggestedCanonical").GetString());
    }

    [Fact]
    public async Task MergeTags_ReportsCount()
    {
        _s.Compressor.Factory = t => new CompressionResult(t, "c", ["stary"], []);
        await _s.Pipeline.CreateFolderAsync("F");
        await _s.Pipeline.AddAsync("F", "a");
        await _s.Pipeline.AddAsync("F", "b");
        var tool = new MergeTagsTool(new TagMerger(_s.Store, _s.Index, _s.Embedder));

        var msg = await tool.ExecuteAsync(Sample.Args(new { fromTags = new[] { "stary" }, toTag = "nowy" }));

        Assert.Equal("Scalono tagi w 'nowy' - zaktualizowano 2 notatek.", msg);
    }

    [Fact]
    public void MergeTags_Describe_ListsTags()
    {
        var text = new MergeTagsTool(new TagMerger(_s.Store, _s.Index, _s.Embedder)).Describe(Sample.Args(new { fromTags = new[] { "a", "b" }, toTag = "c" }));
        Assert.Equal("Scalic tagi [a, b] w tag 'c' we wszystkich notatkach?", text);
    }
}

public class MiscPluginToolsTests : IDisposable
{
    private readonly Stack _s = new();

    public void Dispose() => _s.Dispose();

    [Fact]
    public async Task ListTemplates_ReturnsDefaults()
    {
        var json = await new ListTemplatesTool(new TemplateStore(_s.Root.Notes)).ExecuteAsync(Sample.Args("{}"));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(3, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task FactHistory_ReturnsRecordedVersions()
    {
        var facts = new FactStore(_s.Root.Notes, _s.Bus);
        await facts.RecordFactVersionAsync("Temat", "wersja 1", "zrodlo");

        using var doc = JsonDocument.Parse(await new FactHistoryTool(facts).ExecuteAsync(Sample.Args(new { subject = "temat" })));

        Assert.Equal("wersja 1", doc.RootElement[0].GetProperty("Statement").GetString());
    }

    [Fact]
    public async Task FindDuplicateNotes_DefaultThreshold_ReturnsPairs()
    {
        foreach (var folder in new[] { "A", "B" })
        {
            await _s.Pipeline.CreateFolderAsync(folder);
            await _s.Pipeline.AddAsync(folder, "to samo");
        }
        var tool = new FindDuplicateNotesTool(new DuplicateScanner(_s.Index, _s.Embedder, _s.Store));

        using var doc = JsonDocument.Parse(await tool.ExecuteAsync(Sample.Args("{}")));

        Assert.Equal(1, doc.RootElement.GetArrayLength());
        Assert.Equal(1.0, doc.RootElement[0].GetProperty("Similarity").GetDouble(), 2);
    }

    [Fact]
    public async Task FindDuplicateNotes_HighThreshold_Filters()
    {
        _s.Embedder.Map["C:a"] = [1f, 0f, 0f];
        _s.Embedder.Map["C:b"] = [1f, 0.3f, 0f];
        await _s.Pipeline.CreateFolderAsync("A");
        await _s.Pipeline.CreateFolderAsync("B");
        await _s.Pipeline.AddAsync("A", "a");
        await _s.Pipeline.AddAsync("B", "b");
        var tool = new FindDuplicateNotesTool(new DuplicateScanner(_s.Index, _s.Embedder, _s.Store));

        Assert.Equal("[]", await tool.ExecuteAsync(Sample.Args(new { threshold = 0.99 })));
        using var doc = JsonDocument.Parse(await tool.ExecuteAsync(Sample.Args(new { threshold = 0.9 })));
        Assert.Equal(1, doc.RootElement.GetArrayLength());
    }
}
