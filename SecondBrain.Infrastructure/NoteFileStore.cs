using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

// ponytail: format zapisu jest stały i prosty (kilka pól nagłówka), więc front matter
// czytamy/piszemy ręcznie zamiast ciągnąć zależność YamlDotNet dla tego zakresu.
public class FileNoteStore(NotesRoot notesRoot, IEventBus events) : INoteStore
{
    private const string OriginalHeader = "## Oryginał";
    private const string CompressedHeader = "## Skompresowane (embedowane)";
    private const string TrashSeparator = "___";

    private readonly string _root = notesRoot.Path;

    public async Task<string> SaveAsync(string folder, Note note, CancellationToken ct = default)
    {
        var path = await WriteAsync(folder, note, ct);
        await Changed($"Zapisano notatke: {note.Title}", ct);
        return path;
    }

    private async Task<string> WriteAsync(string folder, Note note, CancellationToken ct)
    {
        var dir = Path.Combine(_root, folder, note.CreatedAt.Year.ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{note.Id}.md");

        await File.WriteAllTextAsync(path, Render(note), ct);
        return path;
    }

    // Kazda mutacja na dysku zglasza sie szynie - backup do gita (GitCommitOnChange) i inni
    // zainteresowani nie musza znac tej klasy.
    private Task Changed(string message, CancellationToken ct) => events.PublishAsync(new StorageChanged(message), ct);

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

        return notes.OrderByDescending(n => n.Pinned).ThenBy(n => n.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<string> MoveAsync(string fromFolder, string toFolder, Note note, CancellationToken ct = default)
    {
        var newPath = await WriteAsync(toFolder, note, ct);
        if (!string.IsNullOrEmpty(note.FilePath) && note.FilePath != newPath && File.Exists(note.FilePath))
            File.Delete(note.FilePath);

        await Changed($"Przeniesiono notatke: {note.Title} ({fromFolder} -> {toFolder})", ct);
        return newPath;
    }

    public async Task DeleteFolderAsync(string folder, CancellationToken ct = default)
    {
        var dir = Path.Combine(_root, folder);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);

        await Changed($"Usunieto folder: {folder}", ct);
    }

    public async Task<string> MoveToTrashAsync(string folder, string filePath, CancellationToken ct = default)
    {
        var note = await LoadAsync(filePath, ct);
        var trashDir = Path.Combine(_root, ".trash");
        Directory.CreateDirectory(trashDir);

        var trashPath = Path.Combine(trashDir, $"{folder}{TrashSeparator}{note.Id}.md");
        File.Move(filePath, trashPath, overwrite: true);
        await Changed($"Notatka w koszu (folder {folder})", ct);
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
        await Changed("Przywrocono notatke z kosza", ct);

        return new TrashedNote(note with { FilePath = restoredPath }, folder, restoredPath);
    }

    public async Task PurgeTrashAsync(string trashPath, CancellationToken ct = default)
    {
        if (File.Exists(trashPath))
            File.Delete(trashPath);

        await Changed("Trwale usunieto notatke z kosza", ct);
    }

    public async Task<IReadOnlyList<AgentSkill>> ListSkillsAsync(CancellationToken ct = default)
    {
        // Wbudowane skille (Skills/ w repo, kopiowane do katalogu aplikacji) + skille uzytkownika
        // z .skills/ w katalogu notatek - skill uzytkownika o tej samej nazwie nadpisuje wbudowany.
        var skills = new Dictionary<string, AgentSkill>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in new[] { Path.Combine(AppContext.BaseDirectory, "Skills"), Path.Combine(_root, ".skills") })
        {
            if (!Directory.Exists(dir))
                continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.md"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                skills[name] = new AgentSkill(name, await File.ReadAllTextAsync(file, ct));
            }
        }

        return skills.Values.OrderBy(s => s.Name).ToList();
    }

    public async Task<IReadOnlyList<FolderedNote>> MergeTagsAsync(string[] fromTags, string toTag, CancellationToken ct = default)
    {
        var fromSet = new HashSet<string>(fromTags, StringComparer.OrdinalIgnoreCase);
        var updated = new List<FolderedNote>();

        if (!Directory.Exists(_root))
            return updated;

        foreach (var folderDir in Directory.EnumerateDirectories(_root))
        {
            var folder = Path.GetFileName(folderDir);
            if (folder.StartsWith('.'))
                continue;

            foreach (var note in await ListAsync(folder, ct))
            {
                if (!note.Tags.Any(t => fromSet.Contains(t)))
                    continue;

                var newTags = note.Tags.Where(t => !fromSet.Contains(t)).Append(toTag)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var merged = note with { Tags = newTags };

                await File.WriteAllTextAsync(merged.FilePath, Render(merged), ct);
                updated.Add(new FolderedNote(folder, merged));
            }
        }

        if (updated.Count > 0)
            await Changed($"Scalono tagi: {string.Join(", ", fromTags)} -> {toTag}", ct);

        return updated;
    }

    // ponytail: slug bez zaleznosci (bez diakrytykow, male litery, myslniki) -
    // wystarczy zeby ten sam termin nadpisywal poprzedni wpis pod ta sama nazwa pliku.
    private static string Slugify(string term)
    {
        var slug = string.Concat(term.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
        return slug.Length > 0 ? slug : Guid.NewGuid().ToString();
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
