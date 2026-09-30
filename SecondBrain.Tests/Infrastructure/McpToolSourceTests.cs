using Microsoft.Extensions.Options;
using SecondBrain.Infrastructure;
using SecondBrain.Infrastructure.AgentTools;

namespace SecondBrain.Tests.Infrastructure;

public class McpToolSourceTests
{
    private static McpToolSource Make(params McpServerOptions[] servers) =>
        new(Options.Create(new PluginsOptions { Mcp = [.. servers] }));

    [Fact]
    public async Task NoServers_NoTools() =>
        Assert.Empty(await Make().ListAsync());

    [Fact]
    public async Task DisabledServer_IsNotStarted()
    {
        var source = Make(new McpServerOptions { Name = "off", Command = "/nie/istnieje", Enabled = false });
        Assert.Empty(await source.ListAsync());
    }

    [Fact]
    public async Task ServerThatCannotStart_IsSkipped_NotFatal()
    {
        var source = Make(
            new McpServerOptions { Name = "zly", Command = "/nie/istnieje-na-pewno", TimeoutSeconds = 5 },
            new McpServerOptions { Name = "wylaczony", Command = "x", Enabled = false });

        Assert.Empty(await source.ListAsync());
        await source.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_WithoutClients_IsSafe() =>
        await Make().DisposeAsync();
}
