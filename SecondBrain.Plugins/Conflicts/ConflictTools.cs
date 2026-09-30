using System.Text.Json;
using SecondBrain.Core;
using SecondBrain.Infrastructure.AgentTools;

namespace SecondBrain.Plugins.Conflicts;

public sealed class FactHistoryTool(FactStore facts) : AgentTool
{
    public override string Name => "fact_history";
    public override string Description => "Pokaz historie sprzecznych wersji faktu dla danego tematu (wykryte wczesniej przez wykrywacz sprzecznosci).";
    protected override string Parameters => """{"type":"object","properties":{"subject":{"type":"string"}},"required":["subject"]}""";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default) =>
        JsonSerializer.Serialize(await facts.ListFactHistoryAsync(args.Req("subject"), ct));
}
