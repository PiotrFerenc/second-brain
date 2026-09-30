using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SecondBrain.Core;
using SecondBrain.Infrastructure;
using SecondBrain.Plugins.Gaps;
using SecondBrain.Plugins.Glossary;
using SecondBrain.Plugins.Search;
using SecondBrain.Plugins.Sdk;
using SecondBrain.Plugins.Tags;
using SecondBrain.Plugins.Timeline;
using SecondBrain.Plugins.Trash;
using SecondBrain.Tests.Support;

namespace SecondBrain.Tests.Plugins;

public class AutoTagOnCompressedTests
{
    private static NoteCompressed Event(IList<string> tags, IReadOnlyList<Note> related, bool fromUser = false) =>
        new("F", "raw", new CompressionResult("t", "c", [], []), tags, fromUser, related);

    [Fact]
    public async Task AddsMostFrequentNeighbourTags_MaxTwo()
    {
        var tags = new List<string> { "moj" };
        var related = new[]
        {
            Sample.Note(tags: ["a", "b", "c"]),
            Sample.Note(tags: ["b", "c"]),
            Sample.Note(tags: ["c", "d"]),
        };

        await new AutoTagOnCompressed().HandleAsync(Event(tags, related));

        Assert.Equal(["moj", "c", "b"], tags);
    }

    [Fact]
    public async Task UserTags_AreNotExtended()
    {
        var tags = new List<string> { "moj" };
        await new AutoTagOnCompressed().HandleAsync(Event(tags, [Sample.Note(tags: ["a"])], fromUser: true));
        Assert.Equal(["moj"], tags);
    }

    [Fact]
    public async Task SkipsTagsAlreadyPresent_CaseInsensitive()
    {
        var tags = new List<string> { "Kot" };
        await new AutoTagOnCompressed().HandleAsync(Event(tags, [Sample.Note(tags: ["kot", "pies"])]));
        Assert.Equal(["Kot", "pies"], tags);
    }

    [Fact]
    public async Task NoRelated_NothingAdded()
    {
        var tags = new List<string> { "a" };
        await new AutoTagOnCompressed().HandleAsync(Event(tags, []));
        Assert.Equal(["a"], tags);
    }

    [Fact]
    public async Task CountsTagsCaseInsensitively()
    {
        var tags = new List<string>();
        await new AutoTagOnCompressed().HandleAsync(Event(tags, [Sample.Note(tags: ["Kot"]), Sample.Note(tags: ["kot"]), Sample.Note(tags: ["pies"])]));
        Assert.Equal("Kot", tags[0]);
    }
}

public class TagFilterTests
{
    [Fact]
    public async Task Apply_SetsFilterAndRefreshes()
    {
        var shell = Substitute.For<IShell>();
        var filter = new TagFilter(shell);

        await filter.ApplyAsync("Kot");

        Assert.True(filter.IsActive);
        Assert.Equal("Kot", filter.ActiveTag);
        Assert.NotNull(shell.TreeFilter);
        await shell.Received(1).RefreshTreeAsync();
    }

    [Fact]
    public async Task Filter_MatchesTagCaseInsensitive()
    {
        var shell = Substitute.For<IShell>();
        await new TagFilter(shell).ApplyAsync("Kot");

        Assert.True(shell.TreeFilter!(Sample.Note(tags: ["kot"])));
        Assert.True(shell.TreeFilter!(Sample.Note(tags: ["x", "KOT"])));
        Assert.False(shell.TreeFilter!(Sample.Note(tags: ["pies"])));
        Assert.False(shell.TreeFilter!(Sample.Note(tags: [])));
    }

    [Fact]
    public async Task Clear_RemovesFilterAndRefreshes()
    {
        var shell = Substitute.For<IShell>();
        var filter = new TagFilter(shell);
        await filter.ApplyAsync("Kot");

        await filter.ClearCommand.ExecuteAsync(null);

        Assert.False(filter.IsActive);
        Assert.Null(filter.ActiveTag);
        Assert.Null(shell.TreeFilter);
        await shell.Received(2).RefreshTreeAsync();
    }

    [Fact]
    public async Task IsActive_RaisesPropertyChanged()
    {
        var filter = new TagFilter(Substitute.For<IShell>());
        var raised = new List<string?>();
        filter.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await filter.ApplyAsync("x");

        Assert.Contains(nameof(TagFilter.IsActive), raised);
    }

    [Fact]
    public void InitiallyInactive() =>
        Assert.False(new TagFilter(Substitute.For<IShell>()).IsActive);
}

public class GapsTabTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly GapStore _gaps;

    public GapsTabTests() => _gaps = new GapStore(_root.Notes, new RecordingEventBus());

    public void Dispose() => _root.Dispose();

    [Fact]
    public void Metadata()
    {
        var tab = new GapsTab(_gaps);
        Assert.Equal("gaps", tab.Id);
        Assert.Equal(TabArea.SidebarToolbar, tab.Placement);
        Assert.Null(tab.Shortcut);
        Assert.Equal("Luki (0)", tab.Title);
    }

    [Fact]
    public async Task Load_FillsCollection_AndUpdatesTitle()
    {
        await _gaps.LogAsync("a");
        await _gaps.LogAsync("b");
        var tab = new GapsTab(_gaps);
        var raised = new List<string?>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await tab.OnActivatedAsync(CancellationToken.None);

        Assert.Equal(2, tab.Gaps.Count);
        Assert.True(tab.HasGaps);
        Assert.Equal("Luki (2)", tab.Title);
        Assert.Contains(nameof(GapsTab.Title), raised);
    }

    [Fact]
    public async Task StorageChanged_Reloads()
    {
        var tab = new GapsTab(_gaps);
        await _gaps.LogAsync("nowa");

        await tab.HandleAsync(new StorageChanged("x"));

        Assert.Single(tab.Gaps);
    }

    [Fact]
    public async Task Resolve_DeletesAndReloads()
    {
        await _gaps.LogAsync("a");
        var tab = new GapsTab(_gaps);
        await tab.OnActivatedAsync(CancellationToken.None);

        await tab.ResolveGapCommand.ExecuteAsync(tab.Gaps[0]);

        Assert.Empty(tab.Gaps);
        Assert.False(tab.HasGaps);
        Assert.Empty(await _gaps.ListAsync());
    }

    [Fact]
    public async Task Resolve_FallsBackToSelectedGap()
    {
        await _gaps.LogAsync("a");
        var tab = new GapsTab(_gaps);
        await tab.OnActivatedAsync(CancellationToken.None);
        tab.SelectedGap = tab.Gaps[0];

        await tab.ResolveGapCommand.ExecuteAsync(null);

        Assert.Empty(tab.Gaps);
        Assert.Null(tab.SelectedGap);
    }

    [Fact]
    public async Task Resolve_NothingSelected_IsNoOp()
    {
        await _gaps.LogAsync("a");
        var tab = new GapsTab(_gaps);
        await tab.OnActivatedAsync(CancellationToken.None);

        await tab.ResolveGapCommand.ExecuteAsync(null);

        Assert.Single(tab.Gaps);
    }

    [Fact]
    public async Task Retry_SearchesQueryInShell()
    {
        await _gaps.LogAsync("pytanie");
        var shell = Substitute.For<IShell>();
        var tab = new GapsTab(_gaps, shell);
        await tab.OnActivatedAsync(CancellationToken.None);

        tab.RetryGapSearchCommand.Execute(tab.Gaps[0]);

        shell.Received(1).Search("pytanie");
    }

    [Fact]
    public async Task Retry_WithoutShell_DoesNotThrow()
    {
        await _gaps.LogAsync("pytanie");
        var tab = new GapsTab(_gaps);
        await tab.OnActivatedAsync(CancellationToken.None);

        tab.RetryGapSearchCommand.Execute(tab.Gaps[0]);
    }
}

public class GlossaryTabTests : IDisposable
{
    private readonly TempRoot _root = new();

    public void Dispose() => _root.Dispose();

    [Fact]
    public async Task Activation_LoadsEntries_EachTime()
    {
        var store = new GlossaryStore(_root.Notes, new RecordingEventBus());
        var tab = new GlossaryTab(store);

        await tab.OnActivatedAsync(CancellationToken.None);
        Assert.False(tab.HasGlossary);

        await store.SaveAsync("RAG", "d", "s");
        await tab.OnActivatedAsync(CancellationToken.None);

        Assert.True(tab.HasGlossary);
        Assert.Equal("RAG", Assert.Single(tab.GlossaryEntries).Term);
    }

    [Fact]
    public void Metadata()
    {
        var tab = new GlossaryTab(new GlossaryStore(_root.Notes, new RecordingEventBus()));
        Assert.Equal("glossary", tab.Id);
        Assert.Equal(40, tab.Order);
    }
}

public class TrashTabTests : IDisposable
{
    private readonly Stack _s = new();

    public void Dispose() => _s.Dispose();

    private TrashTab Tab(IShell? shell = null)
    {
        var sp = new ServiceCollection().AddSingleton(shell ?? Substitute.For<IShell>()).BuildServiceProvider();
        return new TrashTab(_s.Store, _s.Pipeline, sp);
    }

    private async Task<Note> TrashOneAsync(string text)
    {
        await _s.Pipeline.CreateFolderAsync("F");
        var n = (await _s.Pipeline.AddAsync("F", text)).Note;
        await _s.Pipeline.TrashAsync("F", n);
        return n;
    }

    [Fact]
    public async Task Activation_ListsTrash()
    {
        await TrashOneAsync("a");
        var tab = Tab();

        await tab.OnActivatedAsync(CancellationToken.None);

        var item = Assert.Single(tab.TrashItems);
        Assert.Equal("T:a", item.Title);
        Assert.Equal("F", item.OriginalFolder);
        Assert.True(tab.HasTrash);
    }

    [Fact]
    public async Task Events_ReloadTheList()
    {
        var tab = Tab();
        await TrashOneAsync("a");

        await tab.HandleAsync(new NoteTrashed("F", Sample.Note()));
        Assert.Single(tab.TrashItems);

        await tab.HandleAsync(new NotePurged("x"));
        await tab.HandleAsync(new NoteRestored(new TrashedNote(Sample.Note(), "F", "x")));
        Assert.Single(tab.TrashItems);
    }

    [Fact]
    public async Task Restore_MovesNoteBack_AndRefreshesTree()
    {
        await TrashOneAsync("a");
        var shell = Substitute.For<IShell>();
        var tab = Tab(shell);
        await tab.OnActivatedAsync(CancellationToken.None);

        await tab.RestoreFromTrashCommand.ExecuteAsync(tab.TrashItems[0]);

        Assert.Empty(tab.TrashItems);
        Assert.False(tab.HasTrash);
        Assert.Single(await _s.Store.ListAsync("F"));
        await shell.Received(1).RefreshTreeAsync();
    }

    [Fact]
    public async Task Purge_DeletesForever_AndClearsSelection()
    {
        await TrashOneAsync("a");
        var tab = Tab();
        await tab.OnActivatedAsync(CancellationToken.None);
        tab.SelectedTrashItem = tab.TrashItems[0];

        await tab.PurgeFromTrashCommand.ExecuteAsync(null);

        Assert.Empty(tab.TrashItems);
        Assert.Null(tab.SelectedTrashItem);
        Assert.Empty(await _s.Store.ListTrashAsync());
    }

    [Fact]
    public async Task Restore_NothingSelected_IsNoOp()
    {
        await TrashOneAsync("a");
        var tab = Tab();
        await tab.OnActivatedAsync(CancellationToken.None);

        await tab.RestoreFromTrashCommand.ExecuteAsync(null);

        Assert.Single(tab.TrashItems);
    }
}

public class TimelineTabTests : IDisposable
{
    private readonly Stack _s = new();

    public void Dispose() => _s.Dispose();

    private TimelineTab Tab(IServiceProvider? sp = null) =>
        new(_s.Index, _s.Store, sp ?? new ServiceCollection().BuildServiceProvider());

    private async Task SaveAsync(string folder, string title, DateTimeOffset created)
    {
        await _s.Index.CreateFolderAsync(folder);
        await _s.Store.SaveAsync(folder, Sample.Note(title: title, created: created));
    }

    [Fact]
    public async Task Empty_NoGroups()
    {
        var tab = Tab();
        await tab.OnActivatedAsync(CancellationToken.None);

        Assert.Empty(tab.Groups);
        Assert.False(tab.HasTimeline);
    }

    [Fact]
    public async Task GroupsNotesByDay_NewestDayFirst_AcrossFolders()
    {
        await SaveAsync("A", "stary", new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));
        await SaveAsync("B", "nowy1", new DateTimeOffset(2026, 3, 5, 10, 0, 0, TimeSpan.Zero));
        await SaveAsync("B", "nowy2", new DateTimeOffset(2026, 3, 5, 12, 0, 0, TimeSpan.Zero));
        var tab = Tab();

        await tab.HandleAsync(new NotesChanged());

        Assert.True(tab.HasTimeline);
        Assert.Equal(2, tab.Groups.Count);
        Assert.Equal("5 marca 2026", tab.Groups[0].Label);
        Assert.Equal(["nowy2", "nowy1"], tab.Groups[0].Items.Select(i => i.Title));
        Assert.Equal("1 stycznia 2026", tab.Groups[1].Label);
        Assert.Equal("A", tab.Groups[1].Items[0].Folder);
    }

    [Fact]
    public async Task Rebuild_ReplacesPreviousGroups()
    {
        await SaveAsync("A", "a", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var tab = Tab();
        await tab.OnActivatedAsync(CancellationToken.None);
        await SaveAsync("A", "b", new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));

        await tab.OnActivatedAsync(CancellationToken.None);

        Assert.Equal(2, tab.Groups.Count);
    }

    [Fact]
    public async Task OpenNote_ShowsItInShell()
    {
        var shell = Substitute.For<IShell>();
        var sp = new ServiceCollection().AddSingleton(shell).BuildServiceProvider();
        var item = new NoteItem(Guid.NewGuid(), "t", [], 0, "", "", null, false, "F");

        Tab(sp).OpenNoteCommand.Execute(item);

        shell.Received(1).ShowNote(item);
    }

    [Fact]
    public void OpenNote_Null_IsNoOp()
    {
        var shell = Substitute.For<IShell>();
        var sp = new ServiceCollection().AddSingleton(shell).BuildServiceProvider();

        Tab(sp).OpenNoteCommand.Execute(null);

        shell.DidNotReceive().ShowNote(Arg.Any<NoteItem>());
    }
}

public class SearchTabTests : IDisposable
{
    private readonly Stack _s = new();
    private readonly IShell _shell = Substitute.For<IShell>();
    private readonly RecordingEventBus _bus = new();

    public void Dispose() => _s.Dispose();

    private SearchTab Tab(FakeSynthesizer? synth = null) => new(_s.Search, synth ?? _s.Synthesizer, _bus, _shell);

    private async Task SeedAsync(string folder, string text)
    {
        await _s.Pipeline.CreateFolderAsync(folder);
        await _s.Pipeline.AddAsync(folder, text);
    }

    [Fact]
    public void Metadata()
    {
        var tab = Tab();
        Assert.Equal("search", tab.Id);
        Assert.Equal(Avalonia.Input.Key.F, tab.Shortcut!.Key);
        Assert.Equal(Avalonia.Input.KeyModifiers.Control, tab.Shortcut.KeyModifiers);
    }

    [Fact]
    public async Task BlankQuery_MarksSearched_ButFindsNothing()
    {
        var tab = Tab();
        tab.SearchQuery = "   ";

        await tab.SearchCommand.ExecuteAsync(null);

        Assert.True(tab.HasSearched);
        Assert.Empty(tab.SearchResults);
        Assert.Empty(_s.Synthesizer.Calls);
    }

    [Fact]
    public async Task Search_FillsResults_AnswerAndSelection()
    {
        await SeedAsync("F", "kot spi");
        var tab = Tab(new FakeSynthesizer((_, _) => new AnswerResult(true, "kot spi na kanapie")));
        tab.SearchQuery = "kot";

        await tab.SearchCommand.ExecuteAsync(null);

        var result = Assert.Single(tab.SearchResults);
        Assert.Equal("F", result.Folder);
        Assert.Same(result, tab.SelectedResult);
        Assert.True(tab.HasResults);
        Assert.True(tab.HasAnswer);
        Assert.Equal("kot spi na kanapie", tab.SynthesizedAnswer);
        Assert.False(tab.IsBusy);
    }

    [Fact]
    public async Task Search_PublishesSearchCompleted_WithAnsweredFlag()
    {
        await SeedAsync("F", "kot");
        var tab = Tab(new FakeSynthesizer((_, _) => new AnswerResult(false, "brak")));
        tab.SearchQuery = "kot";

        await tab.SearchCommand.ExecuteAsync(null);

        var e = Assert.Single(_bus.Of<SearchCompleted>());
        Assert.Equal("kot", e.Query);
        Assert.False(e.Answered);
    }

    [Fact]
    public async Task Search_NoHits_NoAnswerNoEvent()
    {
        var tab = Tab();
        tab.SearchQuery = "nic";

        await tab.SearchCommand.ExecuteAsync(null);

        Assert.False(tab.HasResults);
        Assert.False(tab.HasAnswer);
        Assert.Empty(_bus.Events);
    }

    [Fact]
    public async Task Search_ClearsPreviousResultsFirst()
    {
        await SeedAsync("F", "kot");
        var tab = Tab();
        tab.SearchQuery = "kot";
        await tab.SearchCommand.ExecuteAsync(null);

        tab.SearchQuery = "";
        await tab.SearchCommand.ExecuteAsync(null);

        Assert.Empty(tab.SearchResults);
        Assert.Null(tab.SelectedResult);
        Assert.Equal("", tab.SynthesizedAnswer);
    }

    [Fact]
    public async Task Search_LimitsResultsToTen()
    {
        for (var i = 0; i < 14; i++)
            await SeedAsync("F", $"kot{i}");
        var tab = Tab();
        tab.SearchQuery = "kot";

        await tab.SearchCommand.ExecuteAsync(null);

        Assert.Equal(10, tab.SearchResults.Count);
        Assert.Equal(5, Assert.Single(_s.Synthesizer.Calls).Notes.Count);
    }

    [Fact]
    public async Task CurrentFolderOnly_UsesShellFolder()
    {
        await SeedAsync("A", "wspolne");
        await SeedAsync("B", "wspolne");
        _shell.SelectedFolder.Returns("B");
        var tab = Tab();
        tab.SearchQuery = "wspolne";
        tab.SearchCurrentFolderOnly = true;

        await tab.SearchCommand.ExecuteAsync(null);

        Assert.All(tab.SearchResults, r => Assert.Equal("B", r.Folder));
        Assert.Single(tab.SearchResults);
    }

    [Fact]
    public async Task CurrentFolderOnly_WithoutSelectedFolder_SearchesEverywhere()
    {
        await SeedAsync("A", "wspolne");
        await SeedAsync("B", "wspolne");
        _shell.SelectedFolder.Returns((string?)null);
        var tab = Tab();
        tab.SearchQuery = "wspolne";
        tab.SearchCurrentFolderOnly = true;

        await tab.SearchCommand.ExecuteAsync(null);

        Assert.Equal(2, tab.SearchResults.Count);
    }

    [Fact]
    public async Task SearchMethod_SetsQueryAndRuns()
    {
        await SeedAsync("F", "kot");
        var tab = Tab();

        tab.Search("kot");
        await Task.Run(async () => { while (!tab.HasResults) await Task.Delay(10); }).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("kot", tab.SearchQuery);
        Assert.Single(tab.SearchResults);
    }

    [Fact]
    public void SelectedFolder_ComesFromShell()
    {
        _shell.SelectedFolder.Returns("X");
        Assert.Equal("X", Tab().SelectedFolder);
    }

    [Fact]
    public async Task OnActivated_RaisesSelectedFolderChanged()
    {
        var tab = Tab();
        var raised = new List<string?>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await tab.OnActivatedAsync(CancellationToken.None);

        Assert.Contains(nameof(SearchTab.SelectedFolder), raised);
    }

    [Fact]
    public void ImplementsSearchTabContract()
    {
        ISearchTab tab = Tab();
        Assert.Null(tab.SelectedResult);
    }
}
