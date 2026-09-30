using System.Text.Json;
using System.Text.Json.Nodes;
using SecondBrain.Core;
using SecondBrain.Infrastructure.AgentTools;

namespace SecondBrain.Tests.Infrastructure;

public class AgentToolRegistryTests
{
    private sealed class T(string name) : IAgentTool
    {
        public string Name => name;
        public string Description => "";
        public JsonObject ParametersSchema => new();
        public bool IsMutating => false;
        public string Describe(JsonElement args) => "";
        public Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default) => Task.FromResult(name);
    }

    private sealed class Source(Func<IReadOnlyList<IAgentTool>> list) : IAgentToolSource
    {
        public int Calls;
        public Task<IReadOnlyList<IAgentTool>> ListAsync(CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(list());
        }
    }

    [Fact]
    public async Task BuiltInAndSources_AreCombined()
    {
        var reg = new AgentToolRegistry([new T("a")], [new Source(() => [new T("b")])]);
        Assert.Equal(["a", "b"], (await reg.ListAsync()).Select(t => t.Name));
    }

    [Fact]
    public async Task NameCollision_BuiltInWins()
    {
        var builtIn = new T("dup");
        var remote = new T("dup");
        var list = await new AgentToolRegistry([builtIn], [new Source(() => [remote])]).ListAsync();

        Assert.Same(builtIn, Assert.Single(list));
    }

    [Fact]
    public async Task NamesAreCaseSensitive()
    {
        var reg = new AgentToolRegistry([new T("a"), new T("A")], []);
        Assert.Equal(2, (await reg.ListAsync()).Count);
    }

    [Fact]
    public async Task ThrowingSource_IsSkipped_OthersStay()
    {
        var reg = new AgentToolRegistry([new T("a")], [
            new Source(() => throw new InvalidOperationException("mcp padl")),
            new Source(() => [new T("ok")])]);

        Assert.Equal(["a", "ok"], (await reg.ListAsync()).Select(t => t.Name));
    }

    [Fact]
    public async Task SourceResult_IsCached()
    {
        var source = new Source(() => [new T("b")]);
        var reg = new AgentToolRegistry([], [source]);

        await reg.ListAsync();
        await reg.ListAsync();

        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task NoToolsAtAll_Empty() =>
        Assert.Empty(await new AgentToolRegistry([], []).ListAsync());
}
