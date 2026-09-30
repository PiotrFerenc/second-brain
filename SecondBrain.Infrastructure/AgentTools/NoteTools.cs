using System.Text.Json;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure.AgentTools;

public sealed class ListNotesTool(INoteStore noteStore) : AgentTool
{
    public override string Name => "list_notes";
    public override string Description => "Wylistuj notatki (id, tytul, tagi) w danym folderze.";
    protected override string Parameters => """{"type":"object","properties":{"folder":{"type":"string"}},"required":["folder"]}""";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var notes = await noteStore.ListAsync(args.Req("folder"), ct);
        return JsonSerializer.Serialize(notes.Select(n => new { n.Id, n.Title, n.Tags, n.Pinned, n.UpdatedAt }));
    }
}

public sealed class GetNoteTool(INoteStore noteStore) : AgentTool
{
    public override string Name => "get_note";
    public override string Description => "Pobierz pelna tresc jednej notatki po id.";
    protected override string Parameters => """{"type":"object","properties":{"folder":{"type":"string"},"noteId":{"type":"string"}},"required":["folder","noteId"]}""";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var notes = await noteStore.ListAsync(args.Req("folder"), ct);
        var noteId = args.Req("noteId");
        var note = notes.FirstOrDefault(n => n.Id.ToString() == noteId);
        return note is null
            ? $"Nie znaleziono notatki {noteId}."
            : JsonSerializer.Serialize(new { note.Id, note.Title, note.CompressedContent, note.Tags, note.Pinned });
    }
}

public sealed class AddNoteTool(NotePipeline pipeline, INoteStore noteStore) : AgentTool
{
    public override string Name => "add_note";
    public override string Description => "Dodaj nowa notatke do folderu - tekst zostanie skompresowany i zindeksowany przez LLM.";
    protected override string Parameters => """{"type":"object","properties":{"folder":{"type":"string"},"text":{"type":"string"},"parentId":{"type":"string","description":"Opcjonalne id notatki-rodzica w tym samym folderze, zeby od razu zagniezdzic nowa notatke."}},"required":["folder","text"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"Dodac notatke do folderu '{S(args, "folder")}': \"{Truncate(S(args, "text"), 100)}\"?";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var folder = args.Req("folder");
        var text = args.Req("text");
        var parentId = args.Opt("parentId") is { } p ? Guid.Parse(p) : (Guid?)null;
        if (parentId is { } pid && !(await noteStore.ListAsync(folder, ct)).Any(n => n.Id == pid))
            return $"Nie znaleziono notatki-rodzica {pid} w folderze '{folder}'.";

        var added = await pipeline.AddAsync(folder, text, parentId, ct: ct);
        return string.Join(" ", added.Notices.Prepend($"Zapisano notatke '{added.Note.Title}' w folderze '{folder}'."));
    }
}

public sealed class EditNoteTool(NotePipeline pipeline, INoteStore noteStore) : AgentTool
{
    public override string Name => "edit_note";
    public override string Description => "Edytuj tresc istniejacej notatki - nowy tekst zostanie skompresowany na nowo (tytul/tagi tez sie przelicza) i podmieni stara tresc.";
    protected override string Parameters => """{"type":"object","properties":{"folder":{"type":"string"},"noteId":{"type":"string"},"text":{"type":"string"}},"required":["folder","noteId","text"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"Zedytowac notatke {S(args, "noteId")} w folderze '{S(args, "folder")}' - nowa tresc: \"{Truncate(S(args, "text"), 100)}\"?";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var folder = args.Req("folder");
        var notes = await noteStore.ListAsync(folder, ct);
        return await NoteToolsShared.EditAsync(pipeline, folder, notes, Guid.Parse(args.Req("noteId")), args.Req("text"), ct);
    }
}

public sealed class SetNotePinnedTool(NotePipeline pipeline, INoteStore noteStore) : AgentTool
{
    public override string Name => "set_note_pinned";
    public override string Description => "Przypnij lub odepnij notatke.";
    protected override string Parameters => """{"type":"object","properties":{"folder":{"type":"string"},"noteId":{"type":"string"},"pinned":{"type":"boolean"}},"required":["folder","noteId","pinned"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"{(args.Bool("pinned") ? "Przypiac" : "Odpiac")} notatke {S(args, "noteId")}?";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var folder = args.Req("folder");
        var noteId = Guid.Parse(args.Req("noteId"));
        var pinned = args.Bool("pinned");

        var notes = await noteStore.ListAsync(folder, ct);
        var existing = notes.FirstOrDefault(n => n.Id == noteId);
        if (existing is null)
            return $"Nie znaleziono notatki {noteId} w folderze '{folder}'.";

        var note = await pipeline.ReindexAsync(folder, existing with { Pinned = pinned, UpdatedAt = DateTimeOffset.UtcNow }, ct);
        return pinned ? $"Przypieto: {note.Title}" : $"Odpieto: {note.Title}";
    }
}

public sealed class MoveNoteTool(NotePipeline pipeline, INoteStore noteStore) : AgentTool
{
    public override string Name => "move_note";
    public override string Description => "Przenies notatke do innego folderu i/lub zagniezdz ja pod inna notatka. Podaj przynajmniej jedno z: targetFolder, newParentId. newParentId='root' odpina notatke na najwyzszy poziom folderu. Notatki z wlasnymi podnotatkami nie mozna przeniesc miedzy folderami.";
    protected override string Parameters => """{"type":"object","properties":{"folder":{"type":"string","description":"Aktualny folder notatki."},"noteId":{"type":"string"},"targetFolder":{"type":"string","description":"Nowy folder notatki."},"newParentId":{"type":"string","description":"Id notatki-rodzica (w folderze docelowym) albo 'root'."}},"required":["folder","noteId"]}""";
    public override bool IsMutating => true;

    public override string Describe(JsonElement args) =>
        $"Przeniesc notatke {S(args, "noteId")}"
        + (string.IsNullOrEmpty(S(args, "targetFolder")) ? "" : $" do folderu '{S(args, "targetFolder")}'")
        + (string.IsNullOrEmpty(S(args, "newParentId")) ? "" : $", nowy rodzic: {S(args, "newParentId")}")
        + "?";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var targetFolder = args.Opt("targetFolder");
        var newParentIdRaw = args.Opt("newParentId");
        if (string.IsNullOrEmpty(targetFolder) && string.IsNullOrEmpty(newParentIdRaw))
            return "Podaj targetFolder i/lub newParentId - nie ma czego zmieniac.";

        return await NoteToolsShared.MoveAsync(pipeline, noteStore, args.Req("folder"), Guid.Parse(args.Req("noteId")), targetFolder, newParentIdRaw, ct);
    }
}

public sealed class GetNoteTreeTool(INoteStore noteStore) : AgentTool
{
    public override string Name => "get_note_tree";
    public override string Description => "Pokaz zagniezdzona strukture notatek (rodzic/dzieci) w danym folderze - przydatne przed uzyciem move_note/add_note z parentId.";
    protected override string Parameters => """{"type":"object","properties":{"folder":{"type":"string"}},"required":["folder"]}""";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var notes = await noteStore.ListAsync(args.Req("folder"), ct);

        object BuildNode(Note n) => new
        {
            n.Id,
            n.Title,
            Children = notes.Where(c => c.ParentId == n.Id).Select(BuildNode).ToList()
        };

        return JsonSerializer.Serialize(notes.Where(n => n.ParentId is null).Select(BuildNode).ToList());
    }
}

public sealed class ListByTagTool(INoteStore noteStore, IVectorIndex vectorIndex) : AgentTool
{
    public override string Name => "list_by_tag";
    public override string Description => "Wylistuj notatki majace dokladnie podany tag (nie semantyczne - dokladne dopasowanie tagu). Bez folderu przeszukuje wszystkie foldery.";
    protected override string Parameters => """{"type":"object","properties":{"tag":{"type":"string"},"folder":{"type":"string"}},"required":["tag"]}""";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var tag = args.Req("tag");
        var folders = args.Opt("folder") is { } f ? (IReadOnlyList<string>)[f] : await vectorIndex.ListFoldersAsync(ct);

        var matches = new List<object>();
        foreach (var folder in folders)
        {
            var notes = await noteStore.ListAsync(folder, ct);
            matches.AddRange(notes.Where(n => n.Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
                .Select(n => new { Folder = folder, n.Id, n.Title, n.Tags }));
        }

        return JsonSerializer.Serialize(matches);
    }
}
