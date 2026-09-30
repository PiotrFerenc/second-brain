using System.Text.Json;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure.AgentTools;

public sealed class BulkImportTool(NotePipeline pipeline) : AgentTool
{
    public override string Name => "bulk_import";
    public override string Description => "Zaimportuj wiele notatek naraz - kazda linia z listy staje sie osobna notatka (kompresja+indeksowanie), bez wykrywania sprzecznosci (za duzo wywolan LLM przy imporcie).";
    protected override string Parameters => """{"type":"object","properties":{"folder":{"type":"string"},"lines":{"type":"array","items":{"type":"string"}}},"required":["folder","lines"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"Zaimportowac {args.GetProperty("lines").EnumerateArray().Count(e => !string.IsNullOrWhiteSpace(e.GetString()))} notatek do folderu '{S(args, "folder")}'?";

    public override Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var lines = args.GetProperty("lines").EnumerateArray()
            .Select(e => (e.GetString() ?? "").Trim()).Where(l => l.Length > 0).ToList();
        return NoteToolsShared.BulkCreateAsync(pipeline, args.Req("folder"), lines, ct);
    }
}

public sealed class BulkNoteCreateTool(NotePipeline pipeline) : AgentTool
{
    public override string Name => "bulk_note_create";
    public override string Description => "Dodaj wiele notatek naraz do jednego folderu - notatki podane jako jeden string, oddzielone przecinkami (kazda staje sie osobna notatka, kompresja+indeksowanie). Prostsza alternatywa dla bulk_import gdy user podaje notatki po przecinku w tekscie.";
    protected override string Parameters => """{"type":"object","properties":{"folder":{"type":"string"},"notes":{"type":"string","description":"Notatki oddzielone przecinkami, np. 'Kup mleko, Zadzwon do Jana, Zaplac czynsz'."}},"required":["folder","notes"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"Dodac {S(args, "notes").Split(',').Count(s => !string.IsNullOrWhiteSpace(s))} notatek do folderu '{S(args, "folder")}'?";

    public override Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var lines = args.Req("notes").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        return NoteToolsShared.BulkCreateAsync(pipeline, args.Req("folder"), lines, ct);
    }
}

public sealed class BulkNoteUpdateTool(NotePipeline pipeline, INoteStore noteStore) : AgentTool
{
    public override string Name => "bulk_note_update";
    public override string Description => "Zaktualizuj wiele notatek naraz w jednym folderze - wpisy podane jako jeden string, oddzielone przecinkami, kazdy w formacie 'noteId: nowa tresc'. Ustal id notatek najpierw przez list_notes/search_notes.";
    protected override string Parameters => """{"type":"object","properties":{"folder":{"type":"string"},"updates":{"type":"string","description":"Wpisy oddzielone przecinkami, np. '3fa8...: Nowa tresc, 9c12...: Inna tresc'."}},"required":["folder","updates"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"Zaktualizowac {S(args, "updates").Split(',').Count(s => !string.IsNullOrWhiteSpace(s))} notatek w folderze '{S(args, "folder")}'?";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var folder = args.Req("folder");
        var notes = await noteStore.ListAsync(folder, ct);

        var results = new List<string>();
        foreach (var entry in args.Req("updates").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
        {
            var sep = entry.IndexOf(':');
            if (sep < 0) { results.Add($"Zly format wpisu: '{entry}' (oczekiwano 'noteId: tekst')."); continue; }

            var idPart = entry[..sep].Trim();
            var text = entry[(sep + 1)..].Trim();
            if (!Guid.TryParse(idPart, out var noteId)) { results.Add($"Niepoprawne id: '{idPart}'."); continue; }

            results.Add(await NoteToolsShared.EditAsync(pipeline, folder, notes, noteId, text, ct));
        }
        return string.Join("\n", results);
    }
}

public sealed class BulkTrashTool(NotePipeline pipeline, INoteStore noteStore) : AgentTool
{
    public override string Name => "bulk_trash";
    public override string Description => "Przenies wiele notatek naraz do kosza (po liscie id, wszystkie w jednym folderze).";
    protected override string Parameters => """{"type":"object","properties":{"folder":{"type":"string"},"noteIds":{"type":"array","items":{"type":"string"}}},"required":["folder","noteIds"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"Przeniesc {SArr(args, "noteIds").Length} notatek z folderu '{S(args, "folder")}' do kosza?";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var folder = args.Req("folder");
        var notes = await noteStore.ListAsync(folder, ct);
        var results = new List<string>();
        foreach (var noteId in args.ReqArr("noteIds").Select(Guid.Parse))
        {
            var note = notes.FirstOrDefault(n => n.Id == noteId);
            if (note is null) { results.Add($"Nie znaleziono notatki {noteId}."); continue; }

            await pipeline.TrashAsync(folder, note, ct);
            results.Add($"Do kosza: {note.Title}");
        }
        return string.Join("\n", results);
    }
}

public sealed class BulkMoveTool(NotePipeline pipeline, INoteStore noteStore) : AgentTool
{
    public override string Name => "bulk_move";
    public override string Description => "Przenies wiele notatek naraz do innego folderu i/lub pod innego rodzica. Podaj przynajmniej jedno z: targetFolder, newParentId.";
    protected override string Parameters => """{"type":"object","properties":{"folder":{"type":"string"},"noteIds":{"type":"array","items":{"type":"string"}},"targetFolder":{"type":"string"},"newParentId":{"type":"string"}},"required":["folder","noteIds"]}""";
    public override bool IsMutating => true;

    public override string Describe(JsonElement args) =>
        $"Przeniesc {SArr(args, "noteIds").Length} notatek"
        + (string.IsNullOrEmpty(S(args, "targetFolder")) ? "" : $" do folderu '{S(args, "targetFolder")}'")
        + (string.IsNullOrEmpty(S(args, "newParentId")) ? "" : $", nowy rodzic: {S(args, "newParentId")}")
        + "?";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var folder = args.Req("folder");
        var targetFolder = args.Opt("targetFolder");
        var newParentIdRaw = args.Opt("newParentId");
        if (string.IsNullOrEmpty(targetFolder) && string.IsNullOrEmpty(newParentIdRaw))
            return "Podaj targetFolder i/lub newParentId - nie ma czego zmieniac.";

        var results = new List<string>();
        foreach (var noteId in args.ReqArr("noteIds").Select(Guid.Parse))
            results.Add(await NoteToolsShared.MoveAsync(pipeline, noteStore, folder, noteId, targetFolder, newParentIdRaw, ct));
        return string.Join("\n", results);
    }
}

public sealed class BulkTagTool(NotePipeline pipeline, INoteStore noteStore) : AgentTool
{
    public override string Name => "bulk_tag";
    public override string Description => "Dodaj i/lub usun tagi na wielu notatkach naraz.";
    protected override string Parameters => """{"type":"object","properties":{"folder":{"type":"string"},"noteIds":{"type":"array","items":{"type":"string"}},"addTags":{"type":"array","items":{"type":"string"}},"removeTags":{"type":"array","items":{"type":"string"}}},"required":["folder","noteIds"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"Zmienic tagi na {SArr(args, "noteIds").Length} notatkach (dodaj: [{string.Join(", ", SArr(args, "addTags"))}], usun: [{string.Join(", ", SArr(args, "removeTags"))}])?";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var folder = args.Req("folder");
        var addTags = args.OptArr("addTags");
        var removeTags = args.OptArr("removeTags");
        var notes = await noteStore.ListAsync(folder, ct);

        var results = new List<string>();
        foreach (var noteId in args.ReqArr("noteIds").Select(Guid.Parse))
        {
            var existing = notes.FirstOrDefault(n => n.Id == noteId);
            if (existing is null) { results.Add($"Nie znaleziono notatki {noteId}."); continue; }

            var newTags = existing.Tags
                .Where(t => !removeTags.Contains(t, StringComparer.OrdinalIgnoreCase))
                .Concat(addTags)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var note = await pipeline.ReindexAsync(folder, existing with { Tags = newTags, UpdatedAt = DateTimeOffset.UtcNow }, ct);
            results.Add($"Zaktualizowano tagi: {note.Title}");
        }
        return string.Join("\n", results);
    }
}

public sealed class BulkPinTool(NotePipeline pipeline, INoteStore noteStore) : AgentTool
{
    public override string Name => "bulk_pin";
    public override string Description => "Przypnij lub odepnij wiele notatek naraz.";
    protected override string Parameters => """{"type":"object","properties":{"folder":{"type":"string"},"noteIds":{"type":"array","items":{"type":"string"}},"pinned":{"type":"boolean"}},"required":["folder","noteIds","pinned"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"{(args.Bool("pinned") ? "Przypiac" : "Odpiac")} {SArr(args, "noteIds").Length} notatek?";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var folder = args.Req("folder");
        var pinned = args.Bool("pinned");
        var notes = await noteStore.ListAsync(folder, ct);

        var results = new List<string>();
        foreach (var noteId in args.ReqArr("noteIds").Select(Guid.Parse))
        {
            var existing = notes.FirstOrDefault(n => n.Id == noteId);
            if (existing is null) { results.Add($"Nie znaleziono notatki {noteId}."); continue; }

            var note = await pipeline.ReindexAsync(folder, existing with { Pinned = pinned, UpdatedAt = DateTimeOffset.UtcNow }, ct);
            results.Add(pinned ? $"Przypieto: {note.Title}" : $"Odpieto: {note.Title}");
        }
        return string.Join("\n", results);
    }
}
