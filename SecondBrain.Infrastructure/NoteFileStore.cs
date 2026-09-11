using Microsoft.Extensions.Options;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

// ponytail: format zapisu jest stały i prosty (kilka pól nagłówka), więc front matter
// czytamy/piszemy ręcznie zamiast ciągnąć zależność YamlDotNet dla tego zakresu.
public class FileNoteStore(IOptions<StorageOptions> options) : INoteStore
{
    private const string OriginalHeader = "## Oryginał";
    private const string CompressedHeader = "## Skompresowane (embedowane)";
    private const string TrashSeparator = "___";

    private static readonly (string Name, string Content)[] DefaultTemplates =
    [
        ("Spotkanie", "## Spotkanie\nData: \nUczestnicy: \n\n### Ustalenia\n- \n\n### Kolejne kroki\n- \n"),
        ("Pomysł", "## Pomysł\n\nProblem: \n\nRozwiązanie: \n\nDlaczego to działa: \n"),
        ("Zadanie", "## Zadanie\n\nCel: \n\nKroki:\n1. \n\nTermin: \n"),
    ];

    private readonly string _root = string.IsNullOrWhiteSpace(options.Value.NotesRootPath)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SecondBrain", "notes")
        : options.Value.NotesRootPath;

    public async Task<string> SaveAsync(string folder, Note note, CancellationToken ct = default)
    {
        var dir = Path.Combine(_root, folder, note.CreatedAt.Year.ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{note.Id}.md");

        await File.WriteAllTextAsync(path, Render(note), ct);
        return path;
    }

    public async Task<Note> LoadAsync(string filePath, CancellationToken ct = default)
    {
        var lines = await File.ReadAllLinesAsync(filePath, ct);
        return Parse(lines, filePath);
    }

    public async Task<IReadOnlyList<Note>> ListAsync(string folder, CancellationToken ct = default)
    {
        var dir = Path.Combine(_root, folder);
        if (!Directory.Exists(dir))
            return [];

        var notes = new List<Note>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories))
            notes.Add(await LoadAsync(file, ct));

        return notes.OrderByDescending(n => n.Pinned).ThenByDescending(n => n.UpdatedAt).ToList();
    }

    public Task DeleteFolderAsync(string folder, CancellationToken ct = default)
    {
        var dir = Path.Combine(_root, folder);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);

        return Task.CompletedTask;
    }

    public async Task<string> MoveToTrashAsync(string folder, string filePath, CancellationToken ct = default)
    {
        var note = await LoadAsync(filePath, ct);
        var trashDir = Path.Combine(_root, ".trash");
        Directory.CreateDirectory(trashDir);

        var trashPath = Path.Combine(trashDir, $"{folder}{TrashSeparator}{note.Id}.md");
        File.Move(filePath, trashPath, overwrite: true);
        return trashPath;
    }

    public async Task<IReadOnlyList<TrashedNote>> ListTrashAsync(CancellationToken ct = default)
    {
        var trashDir = Path.Combine(_root, ".trash");
        if (!Directory.Exists(trashDir))
            return [];

        var result = new List<TrashedNote>();
        foreach (var file in Directory.EnumerateFiles(trashDir, "*.md"))
        {
            var folder = ExtractTrashFolder(file);
            var note = await LoadAsync(file, ct);
            result.Add(new TrashedNote(note, folder, file));
        }

        return result.OrderByDescending(t => t.Note.UpdatedAt).ToList();
    }

    public async Task<TrashedNote> RestoreFromTrashAsync(string trashPath, CancellationToken ct = default)
    {
        var folder = ExtractTrashFolder(trashPath);
        var note = await LoadAsync(trashPath, ct);

        var dir = Path.Combine(_root, folder, note.CreatedAt.Year.ToString());
        Directory.CreateDirectory(dir);
        var restoredPath = Path.Combine(dir, $"{note.Id}.md");
        File.Move(trashPath, restoredPath, overwrite: true);

        return new TrashedNote(note with { FilePath = restoredPath }, folder, restoredPath);
    }

    public Task PurgeTrashAsync(string trashPath, CancellationToken ct = default)
    {
        if (File.Exists(trashPath))
            File.Delete(trashPath);

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<NoteTemplate>> ListTemplatesAsync(CancellationToken ct = default)
    {
        var dir = Path.Combine(_root, ".templates");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            foreach (var (name, content) in DefaultTemplates)
                await File.WriteAllTextAsync(Path.Combine(dir, $"{name}.md"), content, ct);
        }

        var templates = new List<NoteTemplate>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.md").OrderBy(f => f))
            templates.Add(new NoteTemplate(Path.GetFileNameWithoutExtension(file), await File.ReadAllTextAsync(file, ct)));

        return templates;
    }

    public async Task LogGapAsync(string query, CancellationToken ct = default)
    {
        var dir = Path.Combine(_root, ".gaps");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{Guid.NewGuid()}.md");

        var content = $"""
            ---
            asked: {DateTimeOffset.UtcNow:O}
            ---
            {query}
            """;

        await File.WriteAllTextAsync(path, content, ct);
    }

    public async Task<IReadOnlyList<KnowledgeGap>> ListGapsAsync(CancellationToken ct = default)
    {
        var dir = Path.Combine(_root, ".gaps");
        if (!Directory.Exists(dir))
            return [];

        var gaps = new List<KnowledgeGap>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.md"))
        {
            var lines = await File.ReadAllLinesAsync(file, ct);
            if (lines.Length < 3 || lines[0] != "---" || lines[2] != "---")
                continue;

            var askedValue = lines[1][(lines[1].IndexOf(':') + 1)..].Trim();
            var asked = DateTimeOffset.Parse(askedValue);
            var query = string.Join('\n', lines[3..]).Trim();

            gaps.Add(new KnowledgeGap(query, asked, file));
        }

        return gaps.OrderByDescending(g => g.AskedAt).ToList();
    }

    public Task ResolveGapAsync(string path, CancellationToken ct = default)
    {
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    private static string ExtractTrashFolder(string trashPath)
    {
        var name = Path.GetFileNameWithoutExtension(trashPath);
        var sepIndex = name.IndexOf(TrashSeparator, StringComparison.Ordinal);
        return sepIndex > 0 ? name[..sepIndex] : "";
    }

    private static string Render(Note note) => $"""
        ---
        id: {note.Id}
        title: {note.Title}
        tags: [{string.Join(", ", note.Tags)}]
        created: {note.CreatedAt:O}
        updated: {note.UpdatedAt:O}
        parent: {note.ParentId}
        pinned: {note.Pinned}
        ---

        {OriginalHeader}
        {note.RawContent}

        {CompressedHeader}
        {note.CompressedContent}
        """;

    private static Note Parse(string[] lines, string filePath)
    {
        if (lines.Length == 0 || lines[0] != "---")
            throw new FormatException($"Brak front matter w pliku: {filePath}");

        var meta = new Dictionary<string, string>();
        var i = 1;
        for (; lines[i] != "---"; i++)
        {
            var separator = lines[i].IndexOf(':');
            meta[lines[i][..separator].Trim()] = lines[i][(separator + 1)..].Trim();
        }
        i++; // za zamykajacym "---"

        var body = string.Join('\n', lines[i..]);
        var afterOriginal = body[(body.IndexOf(OriginalHeader, StringComparison.Ordinal) + OriginalHeader.Length)..];
        var compressedIndex = afterOriginal.IndexOf(CompressedHeader, StringComparison.Ordinal);

        var raw = afterOriginal[..compressedIndex].Trim();
        var compressed = afterOriginal[(compressedIndex + CompressedHeader.Length)..].Trim();

        var tags = meta["tags"].Trim('[', ']')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var parentId = meta.TryGetValue("parent", out var p) && Guid.TryParse(p, out var parsed) ? parsed : (Guid?)null;
        var pinned = meta.TryGetValue("pinned", out var pin) && bool.TryParse(pin, out var pinnedValue) && pinnedValue;

        return new Note(
            Guid.Parse(meta["id"]),
            meta["title"],
            raw,
            compressed,
            tags,
            DateTimeOffset.Parse(meta["created"]),
            DateTimeOffset.Parse(meta["updated"]),
            filePath,
            parentId,
            pinned);
    }
}
