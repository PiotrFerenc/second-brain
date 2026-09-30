using System.Text.Json;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure.AgentTools;

public sealed class TrashNoteTool(NotePipeline pipeline, INoteStore noteStore) : AgentTool
{
    public override string Name => "trash_note";
    public override string Description => "Przenies notatke do kosza.";
    protected override string Parameters => """{"type":"object","properties":{"folder":{"type":"string"},"noteId":{"type":"string"}},"required":["folder","noteId"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"Przeniesc notatke {S(args, "noteId")} z folderu '{S(args, "folder")}' do kosza?";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var folder = args.Req("folder");
        var noteId = Guid.Parse(args.Req("noteId"));
        var notes = await noteStore.ListAsync(folder, ct);
        var note = notes.FirstOrDefault(n => n.Id == noteId);
        if (note is null)
            return $"Nie znaleziono notatki {noteId} w folderze '{folder}'.";

        await pipeline.TrashAsync(folder, note, ct);
        return $"Przeniesiono do kosza: {note.Title}";
    }
}

public sealed class ListTrashTool(INoteStore noteStore) : AgentTool
{
    public override string Name => "list_trash";
    public override string Description => "Wylistuj notatki w koszu.";
    protected override string Parameters => """{"type":"object","properties":{}}""";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var trashed = await noteStore.ListTrashAsync(ct);
        return JsonSerializer.Serialize(trashed.Select(t => new { t.TrashPath, t.OriginalFolder, t.Note.Title }));
    }
}

public sealed class RestoreNoteTool(NotePipeline pipeline) : AgentTool
{
    public override string Name => "restore_note";
    public override string Description => "Przywroc notatke z kosza do jej pierwotnego folderu.";
    protected override string Parameters => """{"type":"object","properties":{"trashPath":{"type":"string"}},"required":["trashPath"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"Przywrocic notatke z kosza ({S(args, "trashPath")})?";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var restored = await pipeline.RestoreAsync(args.Req("trashPath"), ct);
        return $"Przywrocono '{restored.Note.Title}' do folderu '{restored.OriginalFolder}'.";
    }
}

public sealed class PurgeNoteTool(NotePipeline pipeline) : AgentTool
{
    public override string Name => "purge_note";
    public override string Description => "Trwale usun notatke z kosza. Nieodwracalne.";
    protected override string Parameters => """{"type":"object","properties":{"trashPath":{"type":"string"}},"required":["trashPath"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"TRWALE usunac notatke z kosza ({S(args, "trashPath")})? Tej operacji nie mozna cofnac.";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        await pipeline.PurgeAsync(args.Req("trashPath"), ct);
        return "Usunieto trwale.";
    }
}

public sealed class PurgeTrashAllTool(NotePipeline pipeline, INoteStore noteStore) : AgentTool
{
    public override string Name => "purge_trash_all";
    public override string Description => "Trwale usun WSZYSTKIE notatki z kosza naraz. Nieodwracalne.";
    protected override string Parameters => """{"type":"object","properties":{}}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => "TRWALE usunac WSZYSTKIE notatki z kosza? Tej operacji nie mozna cofnac.";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var trashed = await noteStore.ListTrashAsync(ct);
        foreach (var t in trashed)
            await pipeline.PurgeAsync(t.TrashPath, ct);
        return $"Trwale usunieto {trashed.Count} notatek z kosza.";
    }
}

public sealed class BulkRestoreTool(NotePipeline pipeline) : AgentTool
{
    public override string Name => "bulk_restore";
    public override string Description => "Przywroc wiele notatek z kosza naraz do ich pierwotnych folderow.";
    protected override string Parameters => """{"type":"object","properties":{"trashPaths":{"type":"array","items":{"type":"string"}}},"required":["trashPaths"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"Przywrocic {SArr(args, "trashPaths").Length} notatek z kosza?";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var results = new List<string>();
        foreach (var trashPath in args.ReqArr("trashPaths"))
        {
            var restored = await pipeline.RestoreAsync(trashPath, ct);
            results.Add($"Przywrocono '{restored.Note.Title}' do folderu '{restored.OriginalFolder}'.");
        }
        return string.Join("\n", results);
    }
}

public sealed class BulkPurgeTool(NotePipeline pipeline) : AgentTool
{
    public override string Name => "bulk_purge";
    public override string Description => "Trwale usun z kosza wybrana liste notatek naraz (w przeciwienstwie do purge_trash_all, ktore czysci caly kosz). Nieodwracalne.";
    protected override string Parameters => """{"type":"object","properties":{"trashPaths":{"type":"array","items":{"type":"string"}}},"required":["trashPaths"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"TRWALE usunac {SArr(args, "trashPaths").Length} notatek z kosza? Tej operacji nie mozna cofnac.";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var paths = args.ReqArr("trashPaths");
        foreach (var trashPath in paths)
            await pipeline.PurgeAsync(trashPath, ct);
        return $"Trwale usunieto {paths.Length} notatek z kosza.";
    }
}
