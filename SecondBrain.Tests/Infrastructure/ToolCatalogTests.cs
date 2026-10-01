using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Core;
using SecondBrain.Infrastructure;
using SecondBrain.Infrastructure.AgentTools;
using SecondBrain.Plugins.Sdk;
using SecondBrain.Tests.Support;

namespace SecondBrain.Tests.Infrastructure;

// Kontrakt calego katalogu narzedzi: rdzen + wszystkie pluginy. Lapie literowki w schematach,
// zduplikowane nazwy i narzedzia mutujace bez sensownego opisu pytania Tak/Nie.
[Collection(PluginStateCollection.Name)]
public class ToolCatalogTests : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly TempRoot _root = new();

    public ToolCatalogTests()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:NotesRootPath"] = _root.Path,
            ["Compression:BaseAddress"] = "http://localhost/",
            ["AnswerSynthesis:BaseAddress"] = "http://localhost/",
            ["Agent:BaseAddress"] = "http://localhost/",
            ["Embedding:BaseAddress"] = "http://localhost/",
            ["Reranker:BaseAddress"] = "http://localhost/",
            ["ConflictDetection:BaseAddress"] = "http://localhost/",
            ["TagCleaning:BaseAddress"] = "http://localhost/",
            ["NoteRewrite:BaseAddress"] = "http://localhost/",
            ["Ocr:BaseAddress"] = "http://localhost/",
        }).Build();

        var services = new ServiceCollection();
        services.AddSecondBrainInfrastructure(config);
        var manager = PluginManager.Discover(typeof(SecondBrain.Plugins.Gaps.GapsPlugin).Assembly);
        manager.ConfigureServices(services, config);
        services.AddSingleton<IShell, NullShell>();
        services.AddSingleton<IEditorContext, NullEditorContext>();
        _sp = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _sp.DisposeAsync().AsTask().GetAwaiter().GetResult();   // McpToolSource jest tylko IAsyncDisposable
        _root.Dispose();
    }

    // Argumenty zbudowane ze schematu: kazde wymagane pole dostaje wartosc pasujacego typu.
    private static JsonElement ArgsFor(IAgentTool tool)
    {
        var node = new System.Text.Json.Nodes.JsonObject();
        foreach (var (name, def) in tool.ParametersSchema["properties"]!.AsObject())
        {
            node[name] = def!["type"]!.GetValue<string>() switch
            {
                "array" => new System.Text.Json.Nodes.JsonArray("x"),
                "boolean" => true,
                "number" => 1,
                _ => "x",
            };
        }
        return JsonSerializer.SerializeToElement(node);
    }

    private IReadOnlyList<IAgentTool> Tools => _sp.GetServices<IAgentTool>().ToList();

    [Fact]
    public void ThereAreManyTools() => Assert.True(Tools.Count >= 40, $"tylko {Tools.Count}");

    [Fact]
    public void ToolNames_AreUnique() =>
        Assert.Empty(Tools.GroupBy(t => t.Name).Where(g => g.Count() > 1).Select(g => g.Key));

    [Fact]
    public void ToolNames_MatchApiPattern() =>
        Assert.All(Tools, t => Assert.Matches("^[a-zA-Z0-9_-]{1,64}$", t.Name));

    [Fact]
    public void Descriptions_AreNotBlank() =>
        Assert.All(Tools, t => Assert.False(string.IsNullOrWhiteSpace(t.Description), t.Name));

    [Fact]
    public void Schemas_AreObjectsWithProperties() =>
        Assert.All(Tools, t =>
        {
            var schema = t.ParametersSchema;
            Assert.Equal("object", schema["type"]!.GetValue<string>());
            Assert.NotNull(schema["properties"]);
        });

    [Fact]
    public void Required_ReferencesOnlyDeclaredProperties() =>
        Assert.All(Tools, t =>
        {
            var props = t.ParametersSchema["properties"]!.AsObject().Select(p => p.Key).ToHashSet();
            var required = t.ParametersSchema["required"]?.AsArray().Select(r => r!.GetValue<string>()) ?? [];
            Assert.All(required, r => Assert.True(props.Contains(r), $"{t.Name}: required '{r}' nie jest w properties"));
        });

    [Fact]
    public void Schemas_AreFreshInstancesPerCall()
    {
        var t = Tools.First();
        Assert.NotSame(t.ParametersSchema, t.ParametersSchema);
    }

    [Fact]
    public void MutatingTools_DescribeAsksAQuestion()
    {
        foreach (var tool in Tools.Where(t => t.IsMutating))
        {
            var text = tool.Describe(ArgsFor(tool));
            Assert.False(string.IsNullOrWhiteSpace(text), tool.Name);
            Assert.Contains("?", text);
        }
    }

    [Theory]
    [InlineData("delete_folder")]
    [InlineData("bulk_delete_folders")]
    [InlineData("purge_note")]
    [InlineData("purge_trash_all")]
    public void DestructiveTools_WarnAboutIrreversibility(string name)
    {
        var tool = Tools.Single(t => t.Name == name);
        Assert.True(tool.IsMutating);
        Assert.Contains("nie mozna cofnac", tool.Describe(ArgsFor(tool)));
    }

    [Theory]
    [InlineData("list_folders")]
    [InlineData("list_notes")]
    [InlineData("get_note")]
    [InlineData("search_notes")]
    [InlineData("ask_question")]
    [InlineData("list_gaps")]
    [InlineData("list_glossary")]
    [InlineData("list_trash")]
    [InlineData("list_templates")]
    [InlineData("list_tasks")]
    [InlineData("fact_history")]
    [InlineData("find_duplicate_notes")]
    [InlineData("find_duplicate_tags")]
    public void ReadOnlyTools_DoNotAskForConfirmation(string name) =>
        Assert.False(Tools.Single(t => t.Name == name).IsMutating);

    [Theory]
    [InlineData("add_note")]
    [InlineData("edit_note")]
    [InlineData("move_note")]
    [InlineData("trash_note")]
    [InlineData("merge_tags")]
    [InlineData("bulk_import")]
    [InlineData("resolve_gap")]
    [InlineData("add_glossary_entry")]
    [InlineData("clip_url")]
    public void MutatingTools_AskForConfirmation(string name) =>
        Assert.True(Tools.Single(t => t.Name == name).IsMutating);

    [Fact]
    public void ExpectedPluginTools_ArePresent()
    {
        var names = Tools.Select(t => t.Name).ToHashSet();
        foreach (var expected in new[] { "list_gaps", "list_glossary", "list_trash", "search_notes", "fact_history", "find_duplicate_notes", "merge_tags", "list_templates", "list_tasks", "clip_url", "use_skill" })
            Assert.Contains(expected, names);
    }

    [Fact]
    public async Task Registry_ListsAllRegisteredTools()
    {
        var registry = _sp.GetRequiredService<AgentToolRegistry>();
        Assert.Equal(Tools.Count, (await registry.ListAsync()).Count);
    }

    [Fact]
    public void CoreServices_Resolve()
    {
        Assert.NotNull(_sp.GetRequiredService<NotePipeline>());
        Assert.NotNull(_sp.GetRequiredService<NoteSearch>());
        Assert.NotNull(_sp.GetRequiredService<IAgent>());
        Assert.NotNull(_sp.GetRequiredService<IAgentSessionStore>());
        Assert.IsType<MockEmbedder>(_sp.GetRequiredService<IEmbedder>());
        Assert.IsType<MockReranker>(_sp.GetRequiredService<IReranker>());
    }

    [Fact]
    public void ToolDefinitions_SerializeToValidJson()
    {
        var json = FabrykaAgent.ToolDefinitions(Tools).ToJsonString();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(Tools.Count, doc.RootElement.GetArrayLength());
    }
}
