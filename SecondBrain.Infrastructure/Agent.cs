using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using SecondBrain.Core;
using SecondBrain.Infrastructure.AgentTools;

namespace SecondBrain.Infrastructure;

// Petla agenta (function calling): co wywolac, kiedy pytac o zgode, kiedy skonczyc.
// Same narzedzia to klasy IAgentTool (AgentTools/ + pluginy + MCP) zebrane w AgentToolRegistry.
public class FabrykaAgent(
    IHttpClientFactory httpClientFactory,
    IOptions<AgentOptions> options,
    INoteStore noteStore,
    AgentToolRegistry tools) : IAgent
{
    private readonly AgentOptions _options = options.Value;

    public async Task<AgentStepResult> SendAsync(string conversationState, string userMessage, CancellationToken ct = default)
    {
        var messages = LoadMessages(conversationState);
        messages.Add(new JsonObject { ["role"] = "user", ["content"] = userMessage });
        return await RunLoopAsync(messages, ct);
    }

    public async Task<AgentStepResult> ConfirmAsync(string conversationState, bool approved, CancellationToken ct = default)
    {
        var messages = LoadMessages(conversationState);
        var pendingMessage = messages[^1]!.AsObject();
        var call = ((JsonArray)pendingMessage["tool_calls"]!)[0]!.AsObject();
        var function = call["function"]!.AsObject();

        var resultContent = approved
            ? await ExecuteToolAsync(await ToolsByNameAsync(ct), function["name"]!.GetValue<string>(), function["arguments"]!.GetValue<string>(), ct)
            : "Uzytkownik odrzucil te akcje, nie zostala wykonana.";

        messages.Add(new JsonObject
        {
            ["role"] = "tool",
            ["tool_call_id"] = call["id"]!.GetValue<string>(),
            ["content"] = resultContent
        });

        return await RunLoopAsync(messages, ct);
    }

    // ponytail: twardy limit hopow narzedzi na jedna wiadomosc - zabezpieczenie przed
    // modelem petlacym sie w kolko, nie realny scenariusz przy dzialajacych narzedziach.
    private const int MaxToolHops = 8;

    private async Task<AgentStepResult> RunLoopAsync(JsonArray messages, CancellationToken ct)
    {
        var executedLog = new List<string>();
        var byName = await ToolsByNameAsync(ct);

        for (var hop = 0; hop < MaxToolHops; hop++)
        {
            var assistantMessage = await CallChatCompletionsAsync(messages, byName.Values, ct);
            messages.Add(assistantMessage);

            var toolCalls = assistantMessage["tool_calls"] as JsonArray;
            if (toolCalls is null || toolCalls.Count == 0)
            {
                var text = assistantMessage["content"]?.GetValue<string>() ?? "";
                return new AgentStepResult(Serialize(messages), text, null, executedLog);
            }

            var call = toolCalls[0]!.AsObject();
            var function = call["function"]!.AsObject();
            var toolName = function["name"]!.GetValue<string>();
            var argsJson = function["arguments"]!.GetValue<string>();

            if (byName.TryGetValue(toolName, out var tool) && tool.IsMutating)
            {
                var summary = tool.Describe(JsonDocument.Parse(argsJson).RootElement);
                return new AgentStepResult(Serialize(messages), null, new AgentPendingAction(toolName, argsJson, summary), executedLog);
            }

            var result = await ExecuteToolAsync(byName, toolName, argsJson, ct);
            executedLog.Add($"{toolName}({argsJson})");
            messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = call["id"]!.GetValue<string>(), ["content"] = result });
        }

        return new AgentStepResult(Serialize(messages), "Zbyt wiele krokow narzedzi w jednej turze - przerywam.", null, executedLog);
    }

    private async Task<Dictionary<string, IAgentTool>> ToolsByNameAsync(CancellationToken ct) =>
        (await tools.ListAsync(ct)).ToDictionary(t => t.Name, StringComparer.Ordinal);

    private static async Task<string> ExecuteToolAsync(Dictionary<string, IAgentTool> byName, string toolName, string argsJson, CancellationToken ct) =>
        byName.TryGetValue(toolName, out var tool)
            ? await tool.ExecuteAsync(JsonDocument.Parse(argsJson).RootElement, ct)
            : $"Nieznane narzedzie: {toolName}";

    // Tablica "tools" dla chat/completions - ten sam ksztalt co dawny literal JSON w tej klasie.
    public static JsonArray ToolDefinitions(IEnumerable<IAgentTool> tools) =>
        new(tools.Select(t => (JsonNode)new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["parameters"] = t.ParametersSchema
            }
        }).ToArray());

    private async Task<JsonObject> CallChatCompletionsAsync(JsonArray conversation, IEnumerable<IAgentTool> availableTools, CancellationToken ct)
    {
        var allMessages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = await BuildSystemPromptAsync(ct) } };
        foreach (var m in conversation)
            allMessages.Add(m!.DeepClone());

        var client = httpClientFactory.CreateClient("Agent");
        var requestBody = new JsonObject
        {
            ["model"] = _options.Model,
            ["messages"] = allMessages,
            ["tools"] = ToolDefinitions(availableTools),
            ["tool_choice"] = "auto",
            ["parallel_tool_calls"] = false
        };

        var response = await client.PostAsJsonAsync("chat/completions", requestBody, ct);
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonNode.Parse(raw) ?? throw new InvalidOperationException("Pusta odpowiedz Fabryka chat/completions.");
        var message = doc["choices"]![0]!["message"]!.AsObject();
        return (JsonObject)message.DeepClone();
    }

    // Katalog skilli doklejany do promptu przy kazdym wywolaniu - nowy plik w .skills/
    // dziala od razu, bez restartu. Tylko nazwa + pierwsza linia, reszta przez use_skill.
    private async Task<string> BuildSystemPromptAsync(CancellationToken ct)
    {
        var skills = await noteStore.ListSkillsAsync(ct);
        if (skills.Count == 0)
            return _options.SystemPrompt;

        var catalog = string.Join("\n", skills.Select(s => $"- {s.Name}: {s.Content.Split('\n', 2)[0].Trim()}"));
        return _options.SystemPrompt + "\n\nDostepne skille (gdy zadanie pasuje do opisu, najpierw wywolaj use_skill z nazwa):\n" + catalog;
    }

    private static JsonArray LoadMessages(string conversationState) =>
        string.IsNullOrWhiteSpace(conversationState) ? [] : JsonNode.Parse(conversationState)!.AsArray();

    private static string Serialize(JsonArray messages) => messages.ToJsonString();
}
