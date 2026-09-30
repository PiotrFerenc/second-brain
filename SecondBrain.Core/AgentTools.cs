using System.Text.Json;
using System.Text.Json.Nodes;

namespace SecondBrain.Core;

// Jedno narzedzie agenta (function calling). Wbudowane narzedzia to klasy rejestrowane w DI
// jako IAgentTool (skan asemblera Infrastructure + rejestracje pluginow); narzedzia
// odkrywane w runtime (serwery MCP) przychodza przez IAgentToolSource.
public interface IAgentTool
{
    string Name { get; }                       // [a-zA-Z0-9_-]{1,64} - wymog API chat/completions
    string Description { get; }
    JsonObject ParametersSchema { get; }       // JSON Schema obiektu "parameters"
    bool IsMutating { get; }                   // true = karta Tak/Nie przed wykonaniem
    string Describe(JsonElement args);         // tekst pytania Tak/Nie (tylko gdy IsMutating)
    Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default);
}

public interface IAgentToolSource
{
    Task<IReadOnlyList<IAgentTool>> ListAsync(CancellationToken ct = default);
}

// Pomocnicze odczyty argumentow narzedzia z JSON-a wywolania.
public static class ToolArgs
{
    public static string Req(this JsonElement args, string name) => args.GetProperty(name).GetString()!;

    public static string? Opt(this JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetString() : null;

    public static bool Bool(this JsonElement args, string name) => args.GetProperty(name).GetBoolean();

    public static string[] ReqArr(this JsonElement args, string name) =>
        args.GetProperty(name).EnumerateArray().Select(e => e.GetString() ?? "").ToArray();

    public static string[] OptArr(this JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(e => e.GetString() ?? "").ToArray()
            : [];
}
