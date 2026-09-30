using System.Net;
using Microsoft.Extensions.Options;
using SecondBrain.Core;
using SecondBrain.Infrastructure;
using SecondBrain.Plugins.Conflicts;
using SecondBrain.Plugins.Duplicates;
using SecondBrain.Plugins.Gaps;
using SecondBrain.Plugins.Glossary;
using SecondBrain.Plugins.Ocr;
using SecondBrain.Plugins.Rewrite;
using SecondBrain.Plugins.Tags;
using SecondBrain.Tests.Support;

namespace SecondBrain.Tests.Plugins;

public class DuplicateScannerTests : IDisposable
{
    private readonly Stack _s = new();

    public void Dispose() => _s.Dispose();

    private async Task AddAsync(string folder, string text)
    {
        await _s.Pipeline.CreateFolderAsync(folder);
        await _s.Pipeline.AddAsync(folder, text);
    }

    private DuplicateScanner Scanner => new(_s.Index, _s.Embedder, _s.Store);

    [Fact]
    public async Task NoFolders_NoDuplicates() =>
        Assert.Empty(await Scanner.FindCrossFolderDuplicatesAsync());

    [Fact]
    public async Task IdenticalContentInTwoFolders_IsOnePair()
    {
        await AddAsync("A", "to samo");
        await AddAsync("B", "to samo");

        var pair = Assert.Single(await Scanner.FindCrossFolderDuplicatesAsync());

        Assert.Equal(1f, pair.Similarity, 3);
        Assert.NotEqual(pair.A.Id, pair.B.Id);
    }

    [Fact]
    public async Task IdenticalContentInSameFolder_IsIgnored()
    {
        await AddAsync("A", "to samo");
        await _s.Pipeline.AddAsync("A", "to samo");

        Assert.Empty(await Scanner.FindCrossFolderDuplicatesAsync());
    }

    [Fact]
    public async Task DifferentContent_BelowThreshold_IsIgnored()
    {
        _s.Embedder.Map["C:kot"] = [1f, 0f, 0f];
        _s.Embedder.Map["C:pies"] = [0f, 1f, 0f];
        await AddAsync("A", "kot");
        await AddAsync("B", "pies");

        Assert.Empty(await Scanner.FindCrossFolderDuplicatesAsync());
    }

    [Fact]
    public async Task LowerThreshold_CatchesLooserMatches()
    {
        _s.Embedder.Map["C:kot"] = [1f, 0f, 0f];
        _s.Embedder.Map["C:kotek"] = [1f, 0.5f, 0f];
        await AddAsync("A", "kot");
        await AddAsync("B", "kotek");

        Assert.Empty(await Scanner.FindCrossFolderDuplicatesAsync(0.99f));
        Assert.Single(await Scanner.FindCrossFolderDuplicatesAsync(0.85f));
    }

    [Fact]
    public async Task ThreeFolders_EachPairReportedOnce()
    {
        await AddAsync("A", "x");
        await AddAsync("B", "x");
        await AddAsync("C", "x");

        Assert.Equal(3, (await Scanner.FindCrossFolderDuplicatesAsync()).Count);
    }
}

public class TagMergerTests : IDisposable
{
    private readonly Stack _s = new();

    public void Dispose() => _s.Dispose();

    [Fact]
    public async Task Merge_RewritesFiles_AndRefreshesIndexPayload()
    {
        _s.Compressor.Factory = t => new CompressionResult(t, "c", ["spotkania"], []);
        await _s.Pipeline.CreateFolderAsync("F");
        await _s.Pipeline.AddAsync("F", "a");

        var count = await new TagMerger(_s.Store, _s.Index, _s.Embedder).MergeAsync(["spotkania"], "spotkanie");

        Assert.Equal(1, count);
        Assert.Equal(["spotkanie"], Assert.Single(await _s.Store.ListAsync("F")).Tags);
        var hit = Assert.Single(await _s.Index.SearchAsync("F", [1f, 1f, 0f], 5));
        Assert.Equal(["spotkanie"], hit.Note.Tags);
    }

    [Fact]
    public async Task Merge_NothingMatches_ReturnsZero()
    {
        await _s.Pipeline.CreateFolderAsync("F");
        await _s.Pipeline.AddAsync("F", "a");

        Assert.Equal(0, await new TagMerger(_s.Store, _s.Index, _s.Embedder).MergeAsync(["brak"], "x"));
    }
}

public class TagCleanerTests
{
    private static TagCleaner Make(StubHttpHandler h, StubHttpClientFactory? f = null) =>
        new(f ?? new StubHttpClientFactory(h), Options.Create(new TagCleaningOptions { Model = "tm", SystemPrompt = "SYS" }));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task FewerThanTwoTags_SkipsHttp(int count)
    {
        var h = StubHttpHandler.Chat("{}");
        var r = await Make(h).FindDuplicateGroupsAsync(Enumerable.Range(0, count).Select(i => $"t{i}").ToList());

        Assert.Empty(r);
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task ParsesGroups()
    {
        var h = StubHttpHandler.Chat("""{"groups":[{"tags":["spotkanie","spotkania"],"suggestedCanonical":"spotkanie"}]}""");
        var group = Assert.Single(await Make(h).FindDuplicateGroupsAsync(["spotkanie", "spotkania", "kot"]));

        Assert.Equal(["spotkanie", "spotkania"], group.Tags);
        Assert.Equal("spotkanie", group.SuggestedCanonical);
    }

    [Fact]
    public async Task EmptyOrMissingGroups_ReturnsEmpty()
    {
        Assert.Empty(await Make(StubHttpHandler.Chat("""{"groups":[]}""")).FindDuplicateGroupsAsync(["a", "b"]));
        Assert.Empty(await Make(StubHttpHandler.Chat("{}")).FindDuplicateGroupsAsync(["a", "b"]));
    }

    [Fact]
    public async Task SendsTagsAsCommaSeparatedUserMessage()
    {
        var h = StubHttpHandler.Chat("""{"groups":[]}""");
        var f = new StubHttpClientFactory(h);
        await Make(h, f).FindDuplicateGroupsAsync(["a", "b", "c"]);

        Assert.Equal("TagCleaning", f.LastName);
        using var body = h.LastBody;
        Assert.Equal("a, b, c", body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
        Assert.Equal("tm", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task HttpError_Throws() =>
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Make(StubHttpHandler.Json("{}", HttpStatusCode.TooManyRequests)).FindDuplicateGroupsAsync(["a", "b"]));
}

public class ConflictDetectorTests
{
    private static ConflictDetector Make(StubHttpHandler h, StubHttpClientFactory? f = null) =>
        new(f ?? new StubHttpClientFactory(h), Options.Create(new ConflictDetectionOptions { Model = "cm", SystemPrompt = "SYS" }));

    [Fact]
    public async Task NoCandidates_NoHttp_NoConflict()
    {
        var h = StubHttpHandler.Chat("{}");
        var r = await Make(h).DetectAsync("nowa", []);

        Assert.False(r.HasConflict);
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task ParsesConflict()
    {
        var h = StubHttpHandler.Chat("""{"hasConflict":true,"conflictingTitle":"Spotkanie","explanation":"inna godzina"}""");
        var r = await Make(h).DetectAsync("nowa", [Sample.Note(title: "Spotkanie")]);

        Assert.True(r.HasConflict);
        Assert.Equal("Spotkanie", r.ConflictingTitle);
        Assert.Equal("inna godzina", r.Explanation);
    }

    [Fact]
    public async Task ParsesNoConflictWithNulls()
    {
        var h = StubHttpHandler.Chat("""{"hasConflict":false,"conflictingTitle":null,"explanation":null}""");
        var r = await Make(h).DetectAsync("nowa", [Sample.Note()]);

        Assert.False(r.HasConflict);
        Assert.Null(r.ConflictingTitle);
    }

    [Fact]
    public async Task Prompt_ListsNewAndExistingNotes()
    {
        var h = StubHttpHandler.Chat("""{"hasConflict":false}""");
        var f = new StubHttpClientFactory(h);
        await Make(h, f).DetectAsync("NOWA TRESC", [Sample.Note(title: "Pierwsza", compressed: "c1"), Sample.Note(title: "Druga", compressed: "c2")]);

        Assert.Equal("ConflictDetection", f.LastName);
        using var body = h.LastBody;
        var user = body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
        Assert.StartsWith("NOWA notatka: NOWA TRESC", user);
        Assert.Contains("Notatka 1 (\"Pierwsza\"): c1", user);
        Assert.Contains("Notatka 2 (\"Druga\"): c2", user);
    }

    [Fact]
    public async Task HttpError_Throws() =>
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Make(StubHttpHandler.Json("{}", HttpStatusCode.BadGateway)).DetectAsync("x", [Sample.Note()]));

    [Fact]
    public void Defaults_UseStrongModel_AndDemandJsonFields()
    {
        var o = new ConflictDetectionOptions();
        Assert.Equal("gpt-5", o.Model);
        Assert.Contains("hasConflict", o.SystemPrompt);
    }
}

public class ConflictOnNoteAddedTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly RecordingEventBus _bus = new();

    public void Dispose() => _root.Dispose();

    private (ConflictOnNoteAdded Handler, FactStore Facts, StubHttpHandler Http) Make(string llmJson)
    {
        var http = StubHttpHandler.Chat(llmJson);
        var detector = new ConflictDetector(new StubHttpClientFactory(http), Options.Create(new ConflictDetectionOptions()));
        var facts = new FactStore(_root.Notes, _bus);
        return (new ConflictOnNoteAdded(detector, facts), facts, http);
    }

    private static NoteAdded Event(IReadOnlyList<Note> related, bool fromImport = false, List<string>? notices = null)
    {
        var note = Sample.Note(title: "Nowe", compressed: "o 11:00");
        return new NoteAdded("F", note, new CompressionResult("Nowe", "o 11:00", [], []), related, fromImport, notices ?? []);
    }

    [Fact]
    public async Task FromImport_DoesNothing()
    {
        var (h, _, http) = Make("""{"hasConflict":true,"conflictingTitle":"A","explanation":"x"}""");
        await h.HandleAsync(Event([Sample.Note(title: "A")], fromImport: true));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task NoRelated_DoesNothing()
    {
        var (h, _, http) = Make("""{"hasConflict":true}""");
        await h.HandleAsync(Event([]));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task NoConflict_NoNotice_NoFacts()
    {
        var (h, facts, _) = Make("""{"hasConflict":false}""");
        var notices = new List<string>();

        await h.HandleAsync(Event([Sample.Note(title: "A")], notices: notices));

        Assert.Empty(notices);
        Assert.Empty(await facts.ListFactHistoryAsync("A"));
    }

    [Fact]
    public async Task Conflict_AddsNotice_AndRecordsBothVersions()
    {
        var (h, facts, _) = Make("""{"hasConflict":true,"conflictingTitle":"Spotkanie","explanation":"inna godzina"}""");
        var notices = new List<string>();
        var original = Sample.Note(title: "Spotkanie", compressed: "o 10:00");

        await h.HandleAsync(Event([original], notices: notices));

        Assert.Equal("UWAGA - mozliwa sprzecznosc z \"Spotkanie\": inna godzina", Assert.Single(notices));
        var history = await facts.ListFactHistoryAsync("Spotkanie");
        Assert.Equal(["o 10:00", "o 11:00"], history.Select(v => v.Statement));
    }

    [Fact]
    public async Task SecondConflict_DoesNotDuplicateOriginalVersion()
    {
        var (h, facts, _) = Make("""{"hasConflict":true,"conflictingTitle":"Spotkanie","explanation":"x"}""");
        var original = Sample.Note(title: "Spotkanie", compressed: "o 10:00");

        await h.HandleAsync(Event([original]));
        await h.HandleAsync(Event([original]));

        Assert.Equal(3, (await facts.ListFactHistoryAsync("Spotkanie")).Count);
    }
}

public class GapHandlerTests : IDisposable
{
    private readonly Stack _s = new();

    public void Dispose() => _s.Dispose();

    private GapStore Gaps => new(_s.Root.Notes, _s.Bus);

    [Fact]
    public async Task LogOnSearch_NotAnswered_LogsGap()
    {
        var gaps = Gaps;
        await new GapLogOnSearch(gaps).HandleAsync(new SearchCompleted("Czego brak?", Answered: false));
        Assert.Equal("Czego brak?", Assert.Single(await gaps.ListAsync()).Query);
    }

    [Fact]
    public async Task LogOnSearch_Answered_LogsNothing()
    {
        var gaps = Gaps;
        await new GapLogOnSearch(gaps).HandleAsync(new SearchCompleted("Jest?", Answered: true));
        Assert.Empty(await gaps.ListAsync());
    }

    [Fact]
    public async Task AutoCloser_NoGaps_ReturnsZero_WithoutSearching()
    {
        var closer = new GapAutoCloser(Gaps, _s.Search, _s.Synthesizer);
        Assert.Equal(0, await closer.TryCloseMatchingGapsAsync());
        Assert.Empty(_s.Synthesizer.Calls);
    }

    [Fact]
    public async Task AutoCloser_NoNotesFound_KeepsGap()
    {
        var gaps = Gaps;
        await gaps.LogAsync("pytanie");

        Assert.Equal(0, await new GapAutoCloser(gaps, _s.Search, _s.Synthesizer).TryCloseMatchingGapsAsync());
        Assert.Single(await gaps.ListAsync());
    }

    [Fact]
    public async Task AutoCloser_AnsweredGap_IsClosed_UnansweredStays()
    {
        var gaps = Gaps;
        await gaps.LogAsync("ile lat ma kot");
        await Task.Delay(15);
        await gaps.LogAsync("gdzie mieszka sokrates");
        await _s.Pipeline.CreateFolderAsync("F");
        await _s.Pipeline.AddAsync("F", "kot ma 5 lat");

        var closer = new GapAutoCloser(gaps, _s.Search, new FakeSynthesizer((q, _) => new AnswerResult(q.Contains("kot"), "x")));

        Assert.Equal(1, await closer.TryCloseMatchingGapsAsync());
        Assert.Equal("gdzie mieszka sokrates", Assert.Single(await gaps.ListAsync()).Query);
    }

    [Fact]
    public async Task AutoCloseHandler_AddsNotice_OnClosedGaps()
    {
        var gaps = Gaps;
        await gaps.LogAsync("pytanie o kota");
        await _s.Pipeline.CreateFolderAsync("F");
        await _s.Pipeline.AddAsync("F", "kot");
        var handler = new GapAutoCloseOnNoteAdded(new GapAutoCloser(gaps, _s.Search, new FakeSynthesizer()));
        var notices = new List<string>();
        var e = new NoteAdded("F", Sample.Note(), new CompressionResult("t", "c", [], []), [], FromImport: false, notices);

        await handler.HandleAsync(e);

        Assert.Equal("Zamknieto 1 luk(i) w wiedzy.", Assert.Single(notices));
    }

    [Fact]
    public async Task AutoCloseHandler_SkipsImportedNotes_ButClosesOnImportCompleted()
    {
        var gaps = Gaps;
        await gaps.LogAsync("pytanie o kota");
        await _s.Pipeline.CreateFolderAsync("F");
        await _s.Pipeline.AddAsync("F", "kot");
        var handler = new GapAutoCloseOnNoteAdded(new GapAutoCloser(gaps, _s.Search, new FakeSynthesizer()));

        var perNote = new List<string>();
        await handler.HandleAsync(new NoteAdded("F", Sample.Note(), new CompressionResult("t", "c", [], []), [], FromImport: true, perNote));
        Assert.Empty(perNote);
        Assert.Single(await gaps.ListAsync());

        var summary = new List<string>();
        await handler.HandleAsync(new ImportCompleted("F", 1, summary));
        Assert.Single(summary);
        Assert.Empty(await gaps.ListAsync());
    }
}

public class GlossaryOnNoteAddedTests : IDisposable
{
    private readonly TempRoot _root = new();

    public void Dispose() => _root.Dispose();

    private static CompressionResult Result(params GlossaryTerm[] defs) => new("Tytul notatki", "c", [], defs);
    private static GlossaryTerm D(string t, string d) => new(t, d);

    [Fact]
    public async Task NoteAdded_SavesEachDefinition_WithSourceTitle()
    {
        var store = new GlossaryStore(_root.Notes, new RecordingEventBus());
        var handler = new GlossaryOnNoteAdded(store);

        await handler.HandleAsync(new NoteAdded("F", Sample.Note(), Result(D("A", "def a"), D("B", "def b")), [], false, []));

        var entries = await store.ListAsync();
        Assert.Equal(["A", "B"], entries.Select(e => e.Term));
        Assert.All(entries, e => Assert.Equal("Tytul notatki", e.SourceTitle));
    }

    [Fact]
    public async Task NoteEdited_SavesDefinitions()
    {
        var store = new GlossaryStore(_root.Notes, new RecordingEventBus());
        await new GlossaryOnNoteAdded(store).HandleAsync(new NoteEdited("F", Sample.Note(), Result(D("X", "d")), []));
        Assert.Single(await store.ListAsync());
    }

    [Fact]
    public async Task NullDefinitions_AreTolerated()
    {
        var store = new GlossaryStore(_root.Notes, new RecordingEventBus());
        var result = new CompressionResult("t", "c", [], null!);
        await new GlossaryOnNoteAdded(store).HandleAsync(new NoteAdded("F", Sample.Note(), result, [], false, []));
        Assert.Empty(await store.ListAsync());
    }
}

public class NoteRewriterTests
{
    [Fact]
    public async Task InstructionIsSystem_NoteIsUser_ReturnsTrimmed()
    {
        var h = StubHttpHandler.Chat("  przepisane  \n");
        var f = new StubHttpClientFactory(h);
        var rewriter = new NoteRewriter(f, Options.Create(new NoteRewriteOptions { Model = "rw" }));

        var text = await rewriter.RewriteAsync("popraw styl", "surowy");

        Assert.Equal("przepisane", text);
        Assert.Equal("NoteRewrite", f.LastName);
        using var body = h.LastBody;
        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal("popraw styl", messages[0].GetProperty("content").GetString());
        Assert.Equal("surowy", messages[1].GetProperty("content").GetString());
        Assert.Equal("rw", body.RootElement.GetProperty("model").GetString());
        Assert.False(body.RootElement.TryGetProperty("response_format", out _));
    }

    [Fact]
    public async Task HttpError_Throws() =>
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new NoteRewriter(new StubHttpClientFactory(StubHttpHandler.Json("{}", HttpStatusCode.Unauthorized)), Options.Create(new NoteRewriteOptions())).RewriteAsync("a", "b"));
}

public class LightOnOcrExtractorTests
{
    [Fact]
    public async Task SendsImageAsDataUri_ReturnsTrimmedText()
    {
        var h = StubHttpHandler.Chat("  # Tekst z obrazka \n");
        var f = new StubHttpClientFactory(h);
        var ocr = new LightOnOcrExtractor(f, Options.Create(new OcrOptions { Model = "ocr-m", Prompt = "przepisz" }));

        var text = await ocr.ExtractTextAsync([1, 2, 3], "image/png");

        Assert.Equal("# Tekst z obrazka", text);
        Assert.Equal("Ocr", f.LastName);
        using var body = h.LastBody;
        var content = body.RootElement.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal("przepisz", content[0].GetProperty("text").GetString());
        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(new byte[] { 1, 2, 3 })}", content[1].GetProperty("image_url").GetProperty("url").GetString());
        Assert.Equal("ocr-m", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task HttpError_Throws() =>
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new LightOnOcrExtractor(new StubHttpClientFactory(StubHttpHandler.Json("{}", HttpStatusCode.ServiceUnavailable)), Options.Create(new OcrOptions())).ExtractTextAsync([], "image/png"));

    [Fact]
    public void Defaults()
    {
        var o = new OcrOptions();
        Assert.Equal("LightOnOCR-2-1B", o.Model);
        Assert.False(string.IsNullOrWhiteSpace(o.Prompt));
    }
}
