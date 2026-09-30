using System.Text.Json;
using SecondBrain.Core;
using SecondBrain.Infrastructure.AgentTools;

namespace SecondBrain.Plugins.Tags;

// Narzedzia agenta do czyszczenia tagow - rejestrowane przez plugin, wiec znikaja razem z nim.
public sealed class FindDuplicateTagsTool(INoteStore noteStore, IVectorIndex vectorIndex, TagCleaner tagCleaner) : AgentTool
{
    public override string Name => "find_duplicate_tags";
    public override string Description => "Znajdz grupy potencjalnie zduplikowanych tagow (liczba pojedyncza/mnoga, literowki, synonimy) w calej bazie - kandydaci do merge_tags.";
    protected override string Parameters => """{"type":"object","properties":{}}""";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var allTags = new List<string>();
        foreach (var folder in await vectorIndex.ListFoldersAsync(ct))
            foreach (var note in await noteStore.ListAsync(folder, ct))
                allTags.AddRange(note.Tags);

        var groups = await tagCleaner.FindDuplicateGroupsAsync(allTags.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), ct);
        return JsonSerializer.Serialize(groups);
    }
}

public sealed class MergeTagsTool(TagMerger tagMerger) : AgentTool
{
    public override string Name => "merge_tags";
    public override string Description => "Scal liste tagow w jeden kanoniczny tag we wszystkich notatkach (wszystkie foldery).";
    protected override string Parameters => """{"type":"object","properties":{"fromTags":{"type":"array","items":{"type":"string"}},"toTag":{"type":"string"}},"required":["fromTags","toTag"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"Scalic tagi [{string.Join(", ", SArr(args, "fromTags"))}] w tag '{S(args, "toTag")}' we wszystkich notatkach?";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var toTag = args.Req("toTag");
        var count = await tagMerger.MergeAsync(args.ReqArr("fromTags"), toTag, ct);
        return $"Scalono tagi w '{toTag}' - zaktualizowano {count} notatek.";
    }
}
