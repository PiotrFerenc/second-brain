using SecondBrain.Core;

namespace SecondBrain.Infrastructure.AgentTools;

// Sklejka narzedzi agenta: wbudowane (DI) + zrodla odkrywane w runtime (MCP). Wynik zrodel
// cache'owany po pierwszym udanym wywolaniu; zrodlo, ktore rzuci, jest logowane i pomijane -
// agent dziala dalej na pozostalych. Kolizja nazw: wygrywa wbudowane, duplikat pomijany.
public sealed class AgentToolRegistry(IEnumerable<IAgentTool> builtIn, IEnumerable<IAgentToolSource> sources)
{
    private IReadOnlyList<IAgentTool>? _fromSources;

    public async Task<IReadOnlyList<IAgentTool>> ListAsync(CancellationToken ct = default)
    {
        if (_fromSources is null)
        {
            var discovered = new List<IAgentTool>();
            foreach (var source in sources)
            {
                try
                {
                    discovered.AddRange(await source.ListAsync(ct));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Zrodlo narzedzi agenta {source.GetType().Name} pominiete: {ex.Message}");
                }
            }
            _fromSources = discovered;
        }

        var byName = new Dictionary<string, IAgentTool>(StringComparer.Ordinal);
        foreach (var tool in builtIn.Concat(_fromSources))
        {
            if (!byName.TryAdd(tool.Name, tool))
                Console.Error.WriteLine($"Narzedzie agenta '{tool.Name}' zduplikowane - pomijam drugie ({tool.GetType().Name}).");
        }

        return byName.Values.ToList();
    }
}
