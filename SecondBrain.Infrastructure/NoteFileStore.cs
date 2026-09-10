using Microsoft.Extensions.Options;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

// ponytail: format zapisu jest stały i prosty (5 pól nagłówka), więc front matter
// czytamy/piszemy ręcznie zamiast ciągnąć zależność YamlDotNet dla tego zakresu.
public class FileNoteStore(IOptions<StorageOptions> options) : INoteStore
{
    private const string OriginalHeader = "## Oryginał";
    private const string CompressedHeader = "## Skompresowane (embedowane)";

    private readonly string _root = string.IsNullOrWhiteSpace(options.Value.NotesRootPath)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SecondBrain", "notes")
        : options.Value.NotesRootPath;

    public async Task<string> SaveAsync(string folder, Note note, CancellationToken ct = default)
    {
        var dir = Path.Combine(_root, folder, note.CreatedAt.Year.ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{note.Id}.md");

        var content = $"""
            ---
            id: {note.Id}
            title: {note.Title}
            tags: [{string.Join(", ", note.Tags)}]
            created: {note.CreatedAt:O}
            updated: {note.UpdatedAt:O}
            ---

            {OriginalHeader}
            {note.RawContent}

            {CompressedHeader}
            {note.CompressedContent}
            """;

        await File.WriteAllTextAsync(path, content, ct);
        return path;
    }

    public async Task<Note> LoadAsync(string filePath, CancellationToken ct = default)
    {
        var lines = await File.ReadAllLinesAsync(filePath, ct);

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

        return new Note(
            Guid.Parse(meta["id"]),
            meta["title"],
            raw,
            compressed,
            tags,
            DateTimeOffset.Parse(meta["created"]),
            DateTimeOffset.Parse(meta["updated"]),
            filePath);
    }

    public async Task<IReadOnlyList<Note>> ListAsync(string folder, CancellationToken ct = default)
    {
        var dir = Path.Combine(_root, folder);
        if (!Directory.Exists(dir))
            return [];

        var notes = new List<Note>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories))
            notes.Add(await LoadAsync(file, ct));

        return notes.OrderByDescending(n => n.UpdatedAt).ToList();
    }
}
