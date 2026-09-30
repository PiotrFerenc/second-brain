using SecondBrain.Core;
using SecondBrain.Infrastructure;

namespace SecondBrain.Plugins.Conflicts;

public record FactVersion(DateTimeOffset RecordedAt, string SourceTitle, string Statement);

// Wersjonowanie faktow: .facts/<slug>.md, append-only historia per temat (przeniesione
// z FileNoteStore). Po zapisie StorageChanged, zeby plugin history zrobil commit.
public sealed class FactStore(NotesRoot notesRoot, IEventBus events)
{
    private readonly string _root = notesRoot.Path;

    public async Task RecordFactVersionAsync(string subject, string statement, string sourceTitle, CancellationToken ct = default)
    {
        var dir = Path.Combine(_root, ".facts");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{Slugify(subject)}.md");

        var entry = $"""
            ---
            date: {DateTimeOffset.UtcNow:O}
            source: {sourceTitle}
            ---
            {statement}
            """;

        // Append-only: kazda wersja to kolejny blok front-matter+tresc w tym samym pliku,
        // oddzielony pusta linia zamiast osobnego pliku na wersje - prostszy odczyt calej
        // historii na raz.
        var existing = File.Exists(path) ? await File.ReadAllTextAsync(path, ct) + "\n\n" : "";
        await File.WriteAllTextAsync(path, existing + entry, ct);
        await events.PublishAsync(new StorageChanged($"Wersja faktu: {subject}"), ct);
    }

    public async Task<IReadOnlyList<FactVersion>> ListFactHistoryAsync(string subject, CancellationToken ct = default)
    {
        var path = Path.Combine(_root, ".facts", $"{Slugify(subject)}.md");
        if (!File.Exists(path))
            return [];

        var lines = await File.ReadAllLinesAsync(path, ct);
        var versions = new List<FactVersion>();

        var i = 0;
        while (i < lines.Length)
        {
            if (lines[i] != "---")
            {
                i++;
                continue;
            }

            var date = lines[i + 1][(lines[i + 1].IndexOf(':') + 1)..].Trim();
            var source = lines[i + 2][(lines[i + 2].IndexOf(':') + 1)..].Trim();
            // lines[i + 3] to zamykajace "---" bloku

            var bodyStart = i + 4;
            var bodyEnd = bodyStart;
            while (bodyEnd < lines.Length && lines[bodyEnd] != "---")
                bodyEnd++;

            versions.Add(new FactVersion(DateTimeOffset.Parse(date), source, string.Join('\n', lines[bodyStart..bodyEnd]).Trim()));
            i = bodyEnd;
        }

        return versions.OrderBy(v => v.RecordedAt).ToList();
    }

    // ponytail: slug bez zaleznosci (bez diakrytykow, male litery, myslniki) - kopia
    // z FileNoteStore, nazwa pliku musi zostac ta sama dla istniejacych .facts/.
    private static string Slugify(string term)
    {
        var slug = string.Concat(term.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
        return slug.Length > 0 ? slug : Guid.NewGuid().ToString();
    }
}
