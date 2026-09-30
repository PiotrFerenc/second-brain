using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using NSubstitute;
using SecondBrain.Core;
using SecondBrain.Infrastructure;
using SecondBrain.Infrastructure.AgentTools;
using SecondBrain.Tests.Support;

namespace SecondBrain.Tests.Infrastructure;

public class AgentLoopTests
{
    private sealed class EchoTool(string name, bool mutating) : IAgentTool
    {
        public List<string> Executed { get; } = [];
        public string Name => name;
        public string Description => "opis " + name;
        public JsonObject ParametersSchema => new() { ["type"] = "object" };
        public bool IsMutating => mutating;
        public string Describe(JsonElement args) => $"Czy wykonac {name}? {args.GetRawText()}";

        public Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
        {
            Executed.Add(args.GetRawText());
            return Task.FromResult($"wynik-{name}");
        }
    }

    private static string Reply(string text) =>
        JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = text } } } });

    private static string ToolCall(string name, string args, string id = "call_1") =>
        JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        role = "assistant",
                        content = (string?)null,
                        tool_calls = new[] { new { id, type = "function", function = new { name, arguments = args } } }
                    }
                }
            }
        });

    private static (FabrykaAgent Agent, StubHttpHandler Http) Make(IEnumerable<string> responses, IEnumerable<IAgentTool> tools, IReadOnlyList<AgentSkill>? skills = null)
    {
        var queue = new Queue<string>(responses);
        var http = new StubHttpHandler((_, _) => (HttpStatusCode.OK, queue.Count > 1 ? queue.Dequeue() : queue.Peek()));
        var store = Substitute.For<INoteStore>();
        store.ListSkillsAsync(Arg.Any<CancellationToken>()).Returns(skills ?? []);
        var agent = new FabrykaAgent(new StubHttpClientFactory(http),
            Options.Create(new AgentOptions { Model = "gpt-x", SystemPrompt = "PROMPT" }), store,
            new AgentToolRegistry(tools, []));
        return (agent, http);
    }

    [Fact]
    public async Task PlainReply_ReturnsText_NoPending()
    {
        var (agent, _) = Make([Reply("czesc")], []);
        var r = await agent.SendAsync("", "hej");

        Assert.Equal("czesc", r.ReplyText);
        Assert.Null(r.PendingAction);
        Assert.Empty(r.ExecutedActions);
    }

    [Fact]
    public async Task State_ContainsUserAndAssistantMessages()
    {
        var (agent, _) = Make([Reply("czesc")], []);
        var r = await agent.SendAsync("", "hej");

        var msgs = JsonNode.Parse(r.ConversationState)!.AsArray();
        Assert.Equal(["user", "assistant"], msgs.Select(m => m!["role"]!.GetValue<string>()));
        Assert.Equal("hej", msgs[0]!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task ContinuingConversation_SendsPreviousMessages()
    {
        var (agent, http) = Make([Reply("a"), Reply("b")], []);
        var first = await agent.SendAsync("", "pierwsze");
        await agent.SendAsync(first.ConversationState, "drugie");

        using var body = http.LastBody;
        var contents = body.RootElement.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("role").GetString()!).ToArray();
        Assert.Equal(["system", "user", "assistant", "user"], contents);
    }

    [Fact]
    public async Task Request_HasModelSystemPromptToolsAndNoParallelCalls()
    {
        var (agent, http) = Make([Reply("ok")], [new EchoTool("t1", false)]);
        await agent.SendAsync("", "x");

        using var body = http.LastBody;
        var root = body.RootElement;
        Assert.Equal("gpt-x", root.GetProperty("model").GetString());
        Assert.Equal("auto", root.GetProperty("tool_choice").GetString());
        Assert.False(root.GetProperty("parallel_tool_calls").GetBoolean());
        Assert.Equal("PROMPT", root.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal("t1", root.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
    }

    [Fact]
    public async Task SystemPrompt_AppendsSkillCatalog_FirstLineOnly()
    {
        var (agent, http) = Make([Reply("ok")], [], [new AgentSkill("porzadki", "Sprzatanie tagow\nkrok 1\nkrok 2")]);
        await agent.SendAsync("", "x");

        using var body = http.LastBody;
        var system = body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
        Assert.StartsWith("PROMPT", system);
        Assert.Contains("- porzadki: Sprzatanie tagow", system);
        Assert.DoesNotContain("krok 1", system);
    }

    [Fact]
    public async Task ReadOnlyTool_ExecutesAutomatically_ThenReplies()
    {
        var tool = new EchoTool("czytaj", mutating: false);
        var (agent, _) = Make([ToolCall("czytaj", """{"a":1}"""), Reply("gotowe")], [tool]);

        var r = await agent.SendAsync("", "zrob");

        Assert.Equal("gotowe", r.ReplyText);
        Assert.Equal(["""{"a":1}"""], tool.Executed);
        Assert.Equal(["""czytaj({"a":1})"""], r.ExecutedActions);
        var msgs = JsonNode.Parse(r.ConversationState)!.AsArray();
        Assert.Contains(msgs, m => m!["role"]!.GetValue<string>() == "tool" && m["content"]!.GetValue<string>() == "wynik-czytaj");
    }

    [Fact]
    public async Task MutatingTool_StopsWithPendingAction_WithoutExecuting()
    {
        var tool = new EchoTool("zmien", mutating: true);
        var (agent, _) = Make([ToolCall("zmien", """{"x":"y"}""")], [tool]);

        var r = await agent.SendAsync("", "zmien cos");

        Assert.Null(r.ReplyText);
        Assert.Equal("zmien", r.PendingAction!.ToolName);
        Assert.Equal("""{"x":"y"}""", r.PendingAction.ArgumentsJson);
        Assert.Equal("""Czy wykonac zmien? {"x":"y"}""", r.PendingAction.Summary);
        Assert.Empty(tool.Executed);
    }

    [Fact]
    public async Task Confirm_Approved_ExecutesToolAndContinues()
    {
        var tool = new EchoTool("zmien", mutating: true);
        var (agent, _) = Make([ToolCall("zmien", "{}"), Reply("zrobione")], [tool]);
        var pending = await agent.SendAsync("", "zmien");

        var r = await agent.ConfirmAsync(pending.ConversationState, approved: true);

        Assert.Single(tool.Executed);
        Assert.Equal("zrobione", r.ReplyText);
        Assert.Null(r.PendingAction);
    }

    [Fact]
    public async Task Confirm_Rejected_DoesNotExecute_TellsModel()
    {
        var tool = new EchoTool("zmien", mutating: true);
        var (agent, http) = Make([ToolCall("zmien", "{}"), Reply("dobrze, nie robie")], [tool]);
        var pending = await agent.SendAsync("", "zmien");

        var r = await agent.ConfirmAsync(pending.ConversationState, approved: false);

        Assert.Empty(tool.Executed);
        Assert.Equal("dobrze, nie robie", r.ReplyText);
        using var body = http.LastBody;
        var toolMsg = body.RootElement.GetProperty("messages").EnumerateArray().Last(m => m.GetProperty("role").GetString() == "tool");
        Assert.Contains("odrzucil", toolMsg.GetProperty("content").GetString());
    }

    [Fact]
    public async Task UnknownTool_ReportedToModel_LoopContinues()
    {
        var (agent, _) = Make([ToolCall("nie_ma", "{}"), Reply("ok")], []);
        var r = await agent.SendAsync("", "x");

        Assert.Equal("ok", r.ReplyText);
        var msgs = JsonNode.Parse(r.ConversationState)!.AsArray();
        Assert.Contains(msgs, m => m!["role"]!.GetValue<string>() == "tool" && m["content"]!.GetValue<string>() == "Nieznane narzedzie: nie_ma");
    }

    [Fact]
    public async Task EndlessToolCalls_AreCappedAtEightHops()
    {
        var tool = new EchoTool("czytaj", mutating: false);
        var (agent, http) = Make([ToolCall("czytaj", "{}")], [tool]);

        var r = await agent.SendAsync("", "x");

        Assert.Equal("Zbyt wiele krokow narzedzi w jednej turze - przerywam.", r.ReplyText);
        Assert.Equal(8, http.Requests.Count);
        Assert.Equal(8, tool.Executed.Count);
    }

    [Fact]
    public async Task HttpError_Throws()
    {
        var http = StubHttpHandler.Json("{}", HttpStatusCode.InternalServerError);
        var agent = new FabrykaAgent(new StubHttpClientFactory(http), Options.Create(new AgentOptions()), Substitute.For<INoteStore>(), new AgentToolRegistry([], []));
        await Assert.ThrowsAsync<HttpRequestException>(() => agent.SendAsync("", "x"));
    }

    [Fact]
    public void ToolDefinitions_HaveFunctionShape()
    {
        var defs = FabrykaAgent.ToolDefinitions([new EchoTool("a", false), new EchoTool("b", true)]);

        Assert.Equal(2, defs.Count);
        Assert.Equal("function", defs[0]!["type"]!.GetValue<string>());
        Assert.Equal("a", defs[0]!["function"]!["name"]!.GetValue<string>());
        Assert.Equal("opis b", defs[1]!["function"]!["description"]!.GetValue<string>());
        Assert.Equal("object", defs[1]!["function"]!["parameters"]!["type"]!.GetValue<string>());
    }
}
