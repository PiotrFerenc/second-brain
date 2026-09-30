using System.Text.Json;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure.AgentTools;

// Luki w wiedzy, slownik, fakty, skille, szablony. Magazyny nadal w INoteStore
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

public sealed class ListGlossaryTool(INoteStore noteStore) : AgentTool
{
    public override string Name => "list_glossary";
    public override string Description => "Wylistuj slownik pojec zbudowany automatycznie z notatek.";
    protected override string Parameters => """{"type":"object","properties":{}}""";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default) =>
        JsonSerializer.Serialize(await noteStore.ListGlossaryAsync(ct));
}

public sealed class AddGlossaryEntryTool(INoteStore noteStore) : AgentTool
{
    public override string Name => "add_glossary_entry";
    public override string Description => "Dodaj recznie wpis do slownika pojec (albo nadpisz istniejacy o tym samym terminie).";
    protected override string Parameters => """{"type":"object","properties":{"term":{"type":"string"},"definition":{"type":"string"}},"required":["term","definition"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"Dodac do slownika: '{S(args, "term")}' = \"{Truncate(S(args, "definition"), 100)}\"?";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var term = args.Req("term");
        await noteStore.SaveGlossaryEntryAsync(term, args.Req("definition"), "Reczny wpis", ct);
        return $"Dodano do slownika: {term}";
    }
}

public sealed class DeleteGlossaryEntryTool(INoteStore noteStore) : AgentTool
{
    public override string Name => "delete_glossary_entry";
    public override string Description => "Usun wpis ze slownika pojec po terminie.";
    protected override string Parameters => """{"type":"object","properties":{"term":{"type":"string"}},"required":["term"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"Usunac ze slownika termin '{S(args, "term")}'?";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var term = args.Req("term");
        var deleted = await noteStore.DeleteGlossaryEntryAsync(term, ct);
        return deleted ? $"Usunieto ze slownika: {term}" : $"Nie znaleziono terminu '{term}' w slowniku.";
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
