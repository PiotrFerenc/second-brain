using SecondBrain.Core;
using SecondBrain.Infrastructure;

namespace SecondBrain.Plugins.Glossary;

public record GlossaryEntry(string Term, string Definition, string SourceTitle);

// Magazyn .glossary/ w katalogu notatek: jeden plik na termin (nazwa = slug terminu, wiec ten
// sam termin nadpisuje poprzedni wpis). Po kazdej mutacji StorageChanged - plugin history
// robi z tego commit gita.
public sealed class GlossaryStore(NotesRoot notesRoot, IEventBus events)
{
    private string Dir => Path.Combine(notesRoot.Path, ".glossary");

    public async Task SaveAsync(string term, string definition, string sourceTitle, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Dir);

        var content = $"""
            ---
            term: {term}
            source: {sourceTitle}
            updated: {DateTimeOffset.UtcNow:O}
            ---
            {definition}
            """;

        await File.WriteAllTextAsync(Path.Combine(Dir, $"{Slugify(term)}.md"), content, ct);
        await events.PublishAsync(new StorageChanged($"Slownik: {term}"), ct);
    }

    public async Task<IReadOnlyList<GlossaryEntry>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(Dir))
            return [];

        var entries = new List<GlossaryEntry>();
        foreach (var file in Directory.EnumerateFiles(Dir, "*.md"))
        {
            var lines = await File.ReadAllLinesAsync(file, ct);
            if (lines.Length < 5 || lines[0] != "---" || lines[4] != "---")
                continue;

            var term = lines[1][(lines[1].IndexOf(':') + 1)..].Trim();
            var source = lines[2][(lines[2].IndexOf(':') + 1)..].Trim();
            var definition = string.Join('\n', lines[5..]).Trim();

            entries.Add(new GlossaryEntry(term, definition, source));
        }

        return entries.OrderBy(e => e.Term, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<bool> DeleteAsync(string term, CancellationToken ct = default)
    {
        var path = Path.Combine(Dir, $"{Slugify(term)}.md");
        if (!File.Exists(path))
            return false;

        File.Delete(path);
        await events.PublishAsync(new StorageChanged($"Usunieto z slownika: {term}"), ct);
        return true;
    }

    // ponytail: ta sama funkcja co w FileNoteStore (fakty) - skopiowana, nie wspoldzielona,
    // zeby plugin nie zalezal od prywatnych szczegolow magazynu notatek.
    private static string Slugify(string term)
    {
        var slug = string.Concat(term.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
        return slug.Length > 0 ? slug : Guid.NewGuid().ToString();
    }
}
