using System.Text.Json;
using System.Text.Json.Nodes;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure.AgentTools;

// Baza narzedzi wbudowanych: schemat jako literal JSON (1:1 z tym, co szlo do API przed
// rozbiciem na klasy), domyslnie tylko odczyt bez pytania Tak/Nie.
public abstract class AgentTool : IAgentTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    protected abstract string Parameters { get; }   // JSON Schema obiektu "parameters"
    public virtual bool IsMutating => false;

    public JsonObject ParametersSchema => (JsonObject)JsonNode.Parse(Parameters)!;

    public virtual string Describe(JsonElement args) => $"Wykonac akcje '{Name}' z argumentami {args.GetRawText()}?";

    public abstract Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default);

    protected static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "...";

    // Odczyt do opisu Tak/Nie: brak pola = pusty string (jak dawne S/SArr w DescribeAction).
    protected static string S(JsonElement args, string name) => args.Opt(name) ?? "";
    protected static string[] SArr(JsonElement args, string name) => args.OptArr(name);
}

// Logika wspolna dla kilku narzedzi (pojedyncze i bulk) - jedno miejsce, jak dawniej
// prywatne metody FabrykaAgent.
public static class NoteToolsShared
{
    public static async Task<string> EditAsync(NotePipeline pipeline, string folder, IReadOnlyList<Note> notes, Guid noteId, string text, CancellationToken ct)
    {
        var existing = notes.FirstOrDefault(n => n.Id == noteId);
        if (existing is null)
            return $"Nie znaleziono notatki {noteId} w folderze '{folder}'.";

        var note = await pipeline.EditAsync(folder, existing, text, ct);
        return $"Zaktualizowano notatke: {note.Title}";
    }

    public static async Task<string> BulkCreateAsync(NotePipeline pipeline, string folder, List<string> lines, CancellationToken ct)
    {
        if (lines.Count == 0)
            return "Brak niepustych notatek do zaimportowania.";

        var imported = await pipeline.ImportAsync(folder, lines, ct);
        return string.Join(" ", imported.Notices.Prepend($"Zaimportowano {imported.Count} notatek do '{folder}'."));
    }

    public static async Task<string> MoveAsync(NotePipeline pipeline, INoteStore noteStore, string folder, Guid noteId, string? targetFolder, string? newParentIdRaw, CancellationToken ct)
    {
        var notes = await noteStore.ListAsync(folder, ct);
        var note = notes.FirstOrDefault(n => n.Id == noteId);
        if (note is null)
            return $"Nie znaleziono notatki {noteId} w folderze '{folder}'.";

        var movingFolder = !string.IsNullOrEmpty(targetFolder) && targetFolder != folder;
        if (movingFolder && notes.Any(n => n.ParentId == noteId))
            return "Ta notatka ma podnotatki - przenoszenie miedzy folderami z podnotatkami nie jest wspierane. Najpierw odepnij/przenies dzieci.";

        var newParentId = note.ParentId;
        if (newParentIdRaw is not null)
        {
            if (newParentIdRaw == "root")
            {
                newParentId = null;
            }
            else
            {
                var parentId = Guid.Parse(newParentIdRaw);
                if (parentId == noteId)
                    return "Notatka nie moze byc wlasnym rodzicem.";

                var parentScopeNotes = movingFolder ? await noteStore.ListAsync(targetFolder!, ct) : notes;
                if (parentScopeNotes.All(n => n.Id != parentId))
                    return $"Nie znaleziono notatki-rodzica {parentId} w folderze docelowym.";
                if (!movingFolder && IsDescendant(notes, noteId, parentId))
                    return "Nie mozna zagniezdzic notatki pod jej wlasnym potomkiem.";

                newParentId = parentId;
            }
        }

        var updated = await pipeline.MoveAsync(folder, movingFolder ? targetFolder! : folder,
            note with { ParentId = newParentId, UpdatedAt = DateTimeOffset.UtcNow }, ct);

        return movingFolder
            ? $"Przeniesiono '{updated.Title}' do folderu '{targetFolder}'."
            : $"Zaktualizowano rodzica notatki '{updated.Title}'.";
    }

    private static bool IsDescendant(IReadOnlyList<Note> notes, Guid ancestorId, Guid candidateId)
    {
        var current = notes.FirstOrDefault(n => n.Id == candidateId);
        while (current?.ParentId is { } parentId)
        {
            if (parentId == ancestorId)
                return true;
            current = notes.FirstOrDefault(n => n.Id == parentId);
        }
        return false;
    }
}
