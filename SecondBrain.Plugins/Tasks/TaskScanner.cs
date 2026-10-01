using System.Globalization;
using System.Text.RegularExpressions;
using SecondBrain.Core;

namespace SecondBrain.Plugins.Tasks;

public record TaskEntry(string Folder, Guid NoteId, string NoteTitle, string Text, bool Done, DateOnly? Due);

// Zadania to linie markdown "- [ ] tekst" / "- [x] tekst" w surowej tresci notatek - notatka
// zostaje jedynym zrodlem prawdy, plugin niczego nie przechowuje. Termin = pierwsza data
// RRRR-MM-DD w tekscie zadania (np. "- [ ] zadzwonic do banku 2026-10-05").
public static partial class TaskScanner
{
    [GeneratedRegex(@"^\s*[-*+]\s+\[([ xX])\]\s+(.+?)\s*$")]
    private static partial Regex TaskLine();

    [GeneratedRegex(@"\b(\d{4}-\d{2}-\d{2})\b")]
    private static partial Regex IsoDate();

    public static IEnumerable<TaskEntry> Scan(string folder, Note note)
    {
        foreach (var line in note.RawContent.Split('\n'))
        {
            var m = TaskLine().Match(line.TrimEnd('\r'));
            if (!m.Success)
                continue;

            var text = m.Groups[2].Value;
            DateOnly? due = DateOnly.TryParseExact(IsoDate().Match(text).Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
            yield return new TaskEntry(folder, note.Id, note.Title, text, m.Groups[1].Value != " ", due);
        }
    }

    // Najpierw przeterminowane i najblizsze terminy, na koncu zadania bez daty.
    public static List<TaskEntry> OpenSorted(IEnumerable<TaskEntry> tasks) =>
        tasks.Where(t => !t.Done).OrderBy(t => t.Due ?? DateOnly.MaxValue).ThenBy(t => t.NoteTitle).ToList();

    public static bool IsOverdue(TaskEntry t, DateOnly today) => !t.Done && t.Due is { } d && d < today;

    public static async Task<List<TaskEntry>> ScanAllAsync(IVectorIndex index, INoteStore store, CancellationToken ct = default)
    {
        var all = new List<TaskEntry>();
        foreach (var folder in await index.ListFoldersAsync(ct))
            foreach (var note in await store.ListAsync(folder, ct))
                all.AddRange(Scan(folder, note));
        return all;
    }
}
