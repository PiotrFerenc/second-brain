using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure.AgentTools;

// Jedno zrodlo narzedzi dla wszystkich serwerow MCP z konfiguracji. Serwer, ktory nie wystartuje
// albo nie odpowie w TimeoutSeconds, jest logowany na stderr i pomijany - reszta dziala.
// Klienci (procesy potomne) zyja do zamkniecia aplikacji (IAsyncDisposable przez DI).
public sealed class McpToolSource(IOptions<PluginsOptions> options) : IAgentToolSource, IAsyncDisposable
{
    private readonly List<McpClient> _clients = [];

    public async Task<IReadOnlyList<IAgentTool>> ListAsync(CancellationToken ct = default)
    {
        var tools = new List<IAgentTool>();
        foreach (var server in options.Value.Mcp.Where(s => s.Enabled))
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(server.TimeoutSeconds));

                var transport = new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = server.Name,
                    Command = server.Command,
                    Arguments = server.Args,
                    EnvironmentVariables = server.Env.Count == 0 ? null : server.Env.ToDictionary(kv => kv.Key, kv => (string?)kv.Value),
                });
                var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
                _clients.Add(client);

                foreach (var tool in await client.ListToolsAsync(cancellationToken: timeout.Token))
                    tools.Add(new McpAgentTool(server.Name, client, tool.ProtocolTool));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Serwer MCP '{server.Name}' pominiety: {ex.Message}");
            }
        }
        return tools;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
            await client.DisposeAsync();
    }

    // Narzedzie z serwera MCP widziane przez agenta jak kazde inne. Domyslnie mutujace (karta Tak/Nie) -
    // tylko jawne readOnlyHint=true wykonuje sie bez potwierdzenia; destructiveHint=false nie zwalnia.
    // Tworzone przez zrodlo, nie DI - prywatna klasa zagniezdzona, zeby skan asemblera (tylko typy publiczne) jej nie lapal.
    private sealed class McpAgentTool(string server, McpClient client, Tool tool) : IAgentTool
    {
        // API chat/completions: nazwa funkcji [a-zA-Z0-9_-]{1,64}. Prefiks serwera, bo dwa serwery
        // moga miec narzedzie o tej samej nazwie (read_file).
        public string Name { get; } = Sanitize($"{server}__{tool.Name}");
        public string Description { get; } = tool.Description ?? tool.Title ?? tool.Name;
        public JsonObject ParametersSchema { get; } = JsonNode.Parse(tool.InputSchema.GetRawText())?.AsObject() ?? new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() };
        public bool IsMutating { get; } = tool.Annotations?.ReadOnlyHint != true;
    
        public string Describe(JsonElement args) => $"[{server}] {tool.Name} z argumentami {args.GetRawText()}?";
    
        public async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
        {
            var arguments = args.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize<Dictionary<string, object?>>(args.GetRawText())
                : null;
    
            var result = await client.CallToolAsync(tool.Name, arguments, cancellationToken: ct);
    
            // isError=true: tresc bledu jako zwykly wynik - model ma szanse zareagowac, nie wyjatek.
            var text = string.Join("\n", result.Content.Select(c => c is TextContentBlock t ? t.Text : $"[{c.Type}]"));
            return string.IsNullOrWhiteSpace(text) ? (result.IsError == true ? "Blad narzedzia MCP (bez tresci)." : "(pusty wynik)") : text;
        }
    
        private static string Sanitize(string name)
        {
            var clean = Regex.Replace(name, "[^a-zA-Z0-9_-]", "_");
            return clean.Length <= 64 ? clean : clean[..64];
        }
    }
}
