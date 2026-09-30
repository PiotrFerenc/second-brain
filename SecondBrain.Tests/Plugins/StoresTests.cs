using SecondBrain.Core;
using SecondBrain.Plugins.Conflicts;
using SecondBrain.Plugins.Gaps;
using SecondBrain.Plugins.Glossary;
using SecondBrain.Plugins.Templates;
using SecondBrain.Tests.Support;

namespace SecondBrain.Tests.Plugins;

public class GapStoreTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly RecordingEventBus _bus = new();
    private readonly GapStore _store;

    public GapStoreTests() => _store = new GapStore(_root.Notes, _bus);

    public void Dispose() => _root.Dispose();

    [Fact]
    public async Task List_NoDir_Empty() =>
        Assert.Empty(await _store.ListAsync());

    [Fact]
    public async Task LogThenList_RoundTripsQuery()
    {
        await _store.LogAsync("Kiedy jest spotkanie?");
        var gap = Assert.Single(await _store.ListAsync());

        Assert.Equal("Kiedy jest spotkanie?", gap.Query);
        Assert.True(File.Exists(gap.Path));
        Assert.InRange(gap.AskedAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task Log_MultilineQuery_Preserved()
    {
        await _store.LogAsync("linia 1\nlinia 2");
        Assert.Equal("linia 1\nlinia 2", Assert.Single(await _store.ListAsync()).Query);
    }

    [Fact]
    public async Task Log_PublishesStorageChanged()
    {
        await _store.LogAsync("pytanie");
        Assert.Contains("pytanie", Assert.Single(_bus.Of<StorageChanged>()).Message);
    }

    [Fact]
    public async Task List_NewestFirst()
    {
        await _store.LogAsync("pierwsze");
        await Task.Delay(15);
        await _store.LogAsync("drugie");

        Assert.Equal(["drugie", "pierwsze"], (await _store.ListAsync()).Select(g => g.Query));
    }

    [Fact]
    public async Task List_SkipsMalformedFiles()
    {
        await _store.LogAsync("dobre");
        await File.WriteAllTextAsync(_root.Combine(".gaps", "zle.md"), "bez naglowka");

        Assert.Equal("dobre", Assert.Single(await _store.ListAsync()).Query);
    }

    [Fact]
    public async Task Resolve_DeletesFile_AndPublishes()
    {
        await _store.LogAsync("x");
        var gap = Assert.Single(await _store.ListAsync());
        _bus.Events.Clear();

        await _store.ResolveAsync(gap.Path);

        Assert.False(File.Exists(gap.Path));
        Assert.Empty(await _store.ListAsync());
        Assert.Single(_bus.Of<StorageChanged>());
    }

    [Fact]
    public async Task Resolve_Missing_DoesNotThrow() =>
        await _store.ResolveAsync(_root.Combine("nie-ma.md"));
}

public class GlossaryStoreTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly RecordingEventBus _bus = new();
    private readonly GlossaryStore _store;

    public GlossaryStoreTests() => _store = new GlossaryStore(_root.Notes, _bus);

    public void Dispose() => _root.Dispose();

    [Fact]
    public async Task List_NoDir_Empty() =>
        Assert.Empty(await _store.ListAsync());

    [Fact]
    public async Task SaveThenList_RoundTrips()
    {
        await _store.SaveAsync("RAG", "Retrieval-Augmented Generation", "Notatka o AI");
        var e = Assert.Single(await _store.ListAsync());

        Assert.Equal(new GlossaryEntry("RAG", "Retrieval-Augmented Generation", "Notatka o AI"), e);
    }

    [Fact]
    public async Task Save_SameTermDifferentCase_OverwritesEntry()
    {
        await _store.SaveAsync("RAG", "stara", "a");
        await _store.SaveAsync("rag", "nowa", "b");

        var e = Assert.Single(await _store.ListAsync());
        Assert.Equal("nowa", e.Definition);
    }

    [Fact]
    public async Task Save_MultilineDefinition_Preserved()
    {
        await _store.SaveAsync("T", "a\nb\nc", "s");
        Assert.Equal("a\nb\nc", Assert.Single(await _store.ListAsync()).Definition);
    }

    [Fact]
    public async Task Save_SpecialCharsInTerm_ProduceValidFileName()
    {
        await _store.SaveAsync("C# / .NET?", "def", "s");
        Assert.Equal("C# / .NET?", Assert.Single(await _store.ListAsync()).Term);
    }

    [Fact]
    public async Task Save_BlankTerm_GetsGuidFileName_NotCrash()
    {
        await _store.SaveAsync("   ", "def", "s");
        Assert.Single(await _store.ListAsync());
    }

    [Fact]
    public async Task List_SortedByTermCaseInsensitive()
    {
        await _store.SaveAsync("banan", "d", "s");
        await _store.SaveAsync("Ananas", "d", "s");
        await _store.SaveAsync("cytryna", "d", "s");

        Assert.Equal(["Ananas", "banan", "cytryna"], (await _store.ListAsync()).Select(e => e.Term));
    }

    [Fact]
    public async Task Delete_ExistingAndMissing()
    {
        await _store.SaveAsync("RAG", "d", "s");

        Assert.True(await _store.DeleteAsync("rag"));
        Assert.False(await _store.DeleteAsync("rag"));
        Assert.Empty(await _store.ListAsync());
    }

    [Fact]
    public async Task Mutations_PublishStorageChanged()
    {
        await _store.SaveAsync("RAG", "d", "s");
        await _store.DeleteAsync("RAG");
        await _store.DeleteAsync("RAG");   // nic do usuniecia -> bez zdarzenia

        Assert.Equal(2, _bus.Of<StorageChanged>().Count());
    }

    [Fact]
    public async Task List_SkipsMalformedFiles()
    {
        await _store.SaveAsync("ok", "d", "s");
        await File.WriteAllTextAsync(_root.Combine(".glossary", "zly.md"), "x");
        Assert.Single(await _store.ListAsync());
    }
}

public class FactStoreTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly RecordingEventBus _bus = new();
    private readonly FactStore _store;

    public FactStoreTests() => _store = new FactStore(_root.Notes, _bus);

    public void Dispose() => _root.Dispose();

    [Fact]
    public async Task History_UnknownSubject_Empty() =>
        Assert.Empty(await _store.ListFactHistoryAsync("nic"));

    [Fact]
    public async Task Record_AppendsVersions_InOrder()
    {
        await _store.RecordFactVersionAsync("Spotkanie", "o 10:00", "Notatka A");
        await Task.Delay(15);
        await _store.RecordFactVersionAsync("Spotkanie", "o 11:00", "Notatka B");

        var history = await _store.ListFactHistoryAsync("Spotkanie");

        Assert.Equal(["o 10:00", "o 11:00"], history.Select(v => v.Statement));
        Assert.Equal(["Notatka A", "Notatka B"], history.Select(v => v.SourceTitle));
        Assert.True(history[0].RecordedAt <= history[1].RecordedAt);
    }

    [Fact]
    public async Task Subjects_AreSlugged_CaseInsensitive()
    {
        await _store.RecordFactVersionAsync("Spotkanie Zespołu", "a", "s");
        Assert.Single(await _store.ListFactHistoryAsync("spotkanie zespołu"));
    }

    [Fact]
    public async Task DifferentSubjects_AreSeparate()
    {
        await _store.RecordFactVersionAsync("A", "1", "s");
        await _store.RecordFactVersionAsync("B", "2", "s");

        Assert.Equal("1", Assert.Single(await _store.ListFactHistoryAsync("A")).Statement);
    }

    [Fact]
    public async Task MultilineStatement_Preserved()
    {
        await _store.RecordFactVersionAsync("A", "linia1\nlinia2", "s");
        await _store.RecordFactVersionAsync("A", "druga", "s");

        Assert.Equal(["linia1\nlinia2", "druga"], (await _store.ListFactHistoryAsync("A")).Select(v => v.Statement));
    }

    [Fact]
    public async Task Record_PublishesStorageChanged()
    {
        await _store.RecordFactVersionAsync("Temat", "x", "s");
        Assert.Contains("Temat", Assert.Single(_bus.Of<StorageChanged>()).Message);
    }
}

public class TemplateStoreTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly TemplateStore _store;

    public TemplateStoreTests() => _store = new TemplateStore(_root.Notes);

    public void Dispose() => _root.Dispose();

    [Fact]
    public async Task FirstList_WritesDefaults()
    {
        var templates = await _store.ListAsync();

        Assert.Equal(["Pomysł", "Spotkanie", "Zadanie"], templates.Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.True(File.Exists(_root.Combine(".templates", "Spotkanie.md")));
        Assert.All(templates, t => Assert.False(string.IsNullOrWhiteSpace(t.Content)));
    }

    [Fact]
    public async Task List_DoesNotOverwriteUserEdits()
    {
        await _store.ListAsync();
        await File.WriteAllTextAsync(_root.Combine(".templates", "Spotkanie.md"), "moje");

        var t = (await _store.ListAsync()).Single(x => x.Name == "Spotkanie");

        Assert.Equal("moje", t.Content);
    }

    [Fact]
    public async Task List_PicksUpUserTemplates_IgnoresNonMarkdown()
    {
        await _store.ListAsync();
        await File.WriteAllTextAsync(_root.Combine(".templates", "Moj.md"), "tresc");
        await File.WriteAllTextAsync(_root.Combine(".templates", "notatki.txt"), "nie");

        var names = (await _store.ListAsync()).Select(t => t.Name).ToList();

        Assert.Contains("Moj", names);
        Assert.DoesNotContain("notatki", names);
    }

    [Fact]
    public async Task ExistingDirWithoutFiles_DoesNotRecreateDefaults()
    {
        Directory.CreateDirectory(_root.Combine(".templates"));
        Assert.Empty(await _store.ListAsync());
    }
}
