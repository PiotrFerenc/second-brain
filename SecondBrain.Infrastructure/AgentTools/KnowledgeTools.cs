using System.Text.Json;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure.AgentTools;

// Skille i szablony (luki, slownik, fakty, tagi: ich pluginy). Szablony nadal w INoteStore
// (PLAN-PLUGINS.md: odchudzenie po przeniesieniu tych narzedzi do pluginow).

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
