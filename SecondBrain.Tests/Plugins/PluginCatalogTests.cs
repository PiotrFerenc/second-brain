using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Core;
using SecondBrain.Infrastructure;
using SecondBrain.Plugins.Sdk;
using SecondBrain.Tests.Support;

namespace SecondBrain.Tests.Plugins;

[Collection(PluginStateCollection.Name)]
public class PluginCatalogTests : IDisposable
{
    private static string StatePath => Path.Combine(TestEnvironment.Home, "SecondBrain", "plugins.json");

    private readonly IReadOnlyList<PluginEntry> _plugins;
    private readonly TempRoot _root = new();

    public PluginCatalogTests()
    {
        if (File.Exists(StatePath))
            File.Delete(StatePath);
        _plugins = PluginManager.Discover(typeof(SecondBrain.Plugins.Gaps.GapsPlugin).Assembly).Plugins;
    }

    public void Dispose()
    {
        if (File.Exists(StatePath))
            File.Delete(StatePath);
        _root.Dispose();
    }

    private static readonly string[] Expected =
    [
        "backlinks", "conflicts", "duplicates", "gaps", "glossary", "history", "import",
        "ocr", "quicknote", "rewrite", "search", "tags", "templates", "timeline", "trash",
    ];

    [Fact]
    public void AllExpectedPluginsAreDiscovered() =>
        Assert.Equal(Expected, _plugins.Select(p => p.Id));

    [Fact]
    public void Ids_AreLowercaseAndUnique()
    {
        Assert.All(_plugins, p => Assert.Equal(p.Id.ToLowerInvariant(), p.Id));
        Assert.Equal(_plugins.Count, _plugins.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void NamesAndDescriptions_AreFilled() =>
        Assert.All(_plugins, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Name), p.Id);
            Assert.False(string.IsNullOrWhiteSpace(p.Description), p.Id);
        });

    private ServiceProvider Build(params string[] disabled)
    {
        var manager = PluginManager.Discover(typeof(SecondBrain.Plugins.Gaps.GapsPlugin).Assembly);
        foreach (var id in disabled)
            manager.SetEnabled(id, false);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:NotesRootPath"] = _root.Path,
            ["Compression:BaseAddress"] = "http://localhost/",
            ["AnswerSynthesis:BaseAddress"] = "http://localhost/",
            ["Agent:BaseAddress"] = "http://localhost/",
            ["Embedding:BaseAddress"] = "http://localhost/",
            ["Reranker:BaseAddress"] = "http://localhost/",
        }).Build();
        var services = new ServiceCollection();
        services.AddSecondBrainInfrastructure(config);
        manager.ConfigureServices(services, config);
        services.AddSingleton<IShell, NullShell>();
        services.AddSingleton<IEditorContext, NullEditorContext>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void EveryPlugin_CanConfigureServicesWithEmptyConfig() =>
        Assert.NotNull(Build());

    [Fact]
    public void Tabs_HaveUniqueIdsAndOrders_AndSensiblePlacement()
    {
        using var sp = Build();
        var tabs = sp.GetServices<ITabContribution>().ToList();

        Assert.NotEmpty(tabs);
        Assert.Equal(tabs.Count, tabs.Select(t => t.Id).Distinct().Count());
        Assert.Equal(tabs.Count, tabs.Select(t => t.Order).Distinct().Count());
        Assert.All(tabs, t => Assert.False(string.IsNullOrWhiteSpace(t.Title)));
    }

    [Fact]
    public void Slots_UseOnlyKnownSlotIds()
    {
        string[] known = ["Sidebar.AboveTree", "Editor.Templates", "Editor.Toolbar", "Editor.Footer", "Note.Header", "Note.Actions", "Note.Footer", "Search.Toolbar", "Search.ResultActions"];
        using var sp = Build();

        var slots = sp.GetServices<ISlotContribution>().ToList();

        Assert.NotEmpty(slots);
        Assert.All(slots, s => Assert.Contains(s.SlotId, known));
    }

    [Fact]
    public void DisablingPlugin_RemovesItsContributions()
    {
        using var all = Build();
        using var without = Build("gaps", "trash", "search");

        var tabsBefore = all.GetServices<ITabContribution>().Select(t => t.Id).ToHashSet();
        var tabsAfter = without.GetServices<ITabContribution>().Select(t => t.Id).ToHashSet();

        Assert.Contains("gaps", tabsBefore);
        Assert.DoesNotContain("gaps", tabsAfter);
        Assert.DoesNotContain("trash", tabsAfter);
        Assert.DoesNotContain("search", tabsAfter);
        Assert.Contains("glossary", tabsAfter);
    }

    [Fact]
    public void DisablingPlugin_RemovesItsAgentTools()
    {
        using var all = Build();
        using var without = Build("gaps", "trash");

        var before = all.GetServices<IAgentTool>().Select(t => t.Name).ToHashSet();
        var after = without.GetServices<IAgentTool>().Select(t => t.Name).ToHashSet();

        Assert.Contains("list_gaps", before);
        Assert.DoesNotContain("list_gaps", after);
        Assert.DoesNotContain("trash_note", after);
        Assert.Contains("list_glossary", after);
    }

    [Fact]
    public void DisablingPlugin_RemovesItsEventHandlers()
    {
        using var all = Build();
        using var without = Build("gaps");

        Assert.NotEmpty(all.GetServices<IEventHandler<SearchCompleted>>());
        Assert.Empty(without.GetServices<IEventHandler<SearchCompleted>>());
    }

    [Fact]
    public void AllPluginsDisabled_RegistersNothingFromThem()
    {
        using var sp = Build(Expected);

        Assert.Empty(sp.GetServices<ITabContribution>());
        Assert.Empty(sp.GetServices<ISlotContribution>());
        var tools = sp.GetServices<IAgentTool>().Select(t => t.Name).ToHashSet();
        foreach (var pluginTool in new[] { "list_gaps", "list_glossary", "list_trash", "search_notes", "ask_question", "fact_history", "find_duplicate_notes", "merge_tags", "list_templates" })
            Assert.DoesNotContain(pluginTool, tools);
        Assert.Contains("add_note", tools);   // narzedzia rdzenia zostaja
        Assert.Empty(sp.GetServices<ITrayNewNoteContribution>());
    }
}
