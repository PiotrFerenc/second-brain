using System.Text.Json;
using SecondBrain.Core;
using SecondBrain.Infrastructure.AgentTools;

namespace SecondBrain.Plugins.Gaps;

// Narzedzia agenta pluginu luk - rejestrowane w GapsPlugin, wiec wylaczony plugin ich nie ma.

public sealed class ListGapsTool(GapStore gaps) : AgentTool
{
    public override string Name => "list_gaps";
    public override string Description => "Wylistuj luki w wiedzy - pytania, na ktore baza notatek nie miala jeszcze odpowiedzi.";
    protected override string Parameters => """{"type":"object","properties":{}}""";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default) =>
        JsonSerializer.Serialize(await gaps.ListAsync(ct));
}

public sealed class ResolveGapTool(GapStore gaps) : AgentTool
{
    public override string Name => "resolve_gap";
    public override string Description => "Odrzuc / zamknij luke w wiedzy bez odpowiadania na nia.";
    protected override string Parameters => """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"Odrzucic luke w wiedzy ({S(args, "path")})?";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        await gaps.ResolveAsync(args.Req("path"), ct);
        return "Odrzucono luke.";
    }
}

public sealed class BulkResolveGapsTool(GapStore gaps) : AgentTool
{
    public override string Name => "bulk_resolve_gaps";
    public override string Description => "Odrzuc/zamknij wiele luk w wiedzy naraz bez odpowiadania na nie.";
    protected override string Parameters => """{"type":"object","properties":{"paths":{"type":"array","items":{"type":"string"}}},"required":["paths"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"Odrzucic {SArr(args, "paths").Length} luk w wiedzy?";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var paths = args.ReqArr("paths");
        foreach (var path in paths)
            await gaps.ResolveAsync(path, ct);
        return $"Odrzucono {paths.Length} luk w wiedzy.";
    }
}
