using System.Text.Json;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure.AgentTools;

// Luki w wiedzy, fakty, tagi, skille, szablony (slownik: plugin glossary). Magazyny nadal w INoteStore
// (PLAN-PLUGINS.md: odchudzenie po przeniesieniu tych narzedzi do pluginow).

public sealed class ListGapsTool(INoteStore noteStore) : AgentTool
{
    public override string Name => "list_gaps";
    public override string Description => "Wylistuj luki w wiedzy - pytania, na ktore baza notatek nie miala jeszcze odpowiedzi.";
    protected override string Parameters => """{"type":"object","properties":{}}""";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default) =>
        JsonSerializer.Serialize(await noteStore.ListGapsAsync(ct));
}

public sealed class ResolveGapTool(INoteStore noteStore) : AgentTool
{
    public override string Name => "resolve_gap";
    public override string Description => "Odrzuc / zamknij luke w wiedzy bez odpowiadania na nia.";
    protected override string Parameters => """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"Odrzucic luke w wiedzy ({S(args, "path")})?";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        await noteStore.ResolveGapAsync(args.Req("path"), ct);
        return "Odrzucono luke.";
    }
}

public sealed class BulkResolveGapsTool(INoteStore noteStore) : AgentTool
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
            await noteStore.ResolveGapAsync(path, ct);
        return $"Odrzucono {paths.Length} luk w wiedzy.";
    }
}

public sealed class FactHistoryTool(INoteStore noteStore) : AgentTool
{
    public override string Name => "fact_history";
    public override string Description => "Pokaz historie sprzecznych wersji faktu dla danego tematu (wykryte wczesniej przez wykrywacz sprzecznosci).";
    protected override string Parameters => """{"type":"object","properties":{"subject":{"type":"string"}},"required":["subject"]}""";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default) =>
        JsonSerializer.Serialize(await noteStore.ListFactHistoryAsync(args.Req("subject"), ct));
}

public sealed class FindDuplicateTagsTool(INoteStore noteStore, IVectorIndex vectorIndex, ITagCleaner tagCleaner) : AgentTool
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

public sealed class UseSkillTool(INoteStore noteStore) : AgentTool
{
    public override string Name => "use_skill";
    public override string Description => "Zaladuj pelna instrukcje skilla (z listy dostepnych skilli w prompcie systemowym) i postepuj wedlug niej.";
    protected override string Parameters => """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var name = args.Req("name");
        var skill = (await noteStore.ListSkillsAsync(ct)).FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return skill?.Content ?? $"Nie ma skilla '{name}'.";
    }
}

// Nowe (po rozbiciu): dowod, ze narzedzie = jedna klasa, bez zmian w petli agenta/DI/CLI.
public sealed class ListSkillsTool(INoteStore noteStore) : AgentTool
{
    public override string Name => "list_skills";
    public override string Description => "Wylistuj dostepne skille agenta (nazwa + jednolinijkowy opis), bez ladowania instrukcji.";
    protected override string Parameters => """{"type":"object","properties":{}}""";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var skills = await noteStore.ListSkillsAsync(ct);
        return JsonSerializer.Serialize(skills.Select(s => new { s.Name, Description = s.Content.Split('\n', 2)[0].Trim() }));
    }
}

public sealed class ListTemplatesTool(INoteStore noteStore) : AgentTool
{
    public override string Name => "list_templates";
    public override string Description => "Wylistuj szablony notatek (nazwa + tresc) dostepne do wykorzystania przed dodaniem notatki.";
    protected override string Parameters => """{"type":"object","properties":{}}""";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default) =>
        JsonSerializer.Serialize(await noteStore.ListTemplatesAsync(ct));
}
