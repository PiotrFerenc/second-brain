using System.Text.Json;
using SecondBrain.Infrastructure.AgentTools;

namespace SecondBrain.Plugins.Duplicates;

public sealed class FindDuplicateNotesTool(DuplicateScanner duplicateScanner) : AgentTool
{
    public override string Name => "find_duplicate_notes";
    public override string Description => "Znajdz notatki niemal identyczne (semantycznie) zyjace w dwoch roznych folderach - kandydaci do recznego scalenia.";
    protected override string Parameters => """{"type":"object","properties":{"threshold":{"type":"number","description":"Prog podobienstwa 0-1, domyslnie 0.92."}}}""";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var threshold = args.TryGetProperty("threshold", out var t) ? (float)t.GetDouble() : 0.92f;
        var dups = await duplicateScanner.FindCrossFolderDuplicatesAsync(threshold, ct);
        return JsonSerializer.Serialize(dups.Select(d => new
        {
            A = new { d.A.Id, d.A.Title },
            B = new { d.B.Id, d.B.Title },
            d.Similarity
        }));
    }
}
