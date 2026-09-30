using SecondBrain.Core;
using SecondBrain.Infrastructure;

namespace SecondBrain.Plugins.Gaps;

public record KnowledgeGap(string Query, DateTimeOffset AskedAt, string Path);

// Magazyn luk: <root>/.gaps/<guid>.md (front matter z data + tresc pytania). Kod 1:1 z dawnego
// FileNoteStore; po kazdej mutacji StorageChanged, z ktorego plugin history robi commit.
public sealed class GapStore(NotesRoot notesRoot, IEventBus events)
{
    private string Dir => Path.Combine(notesRoot.Path, ".gaps");

    public async Task LogAsync(string query, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Dir);
        var path = Path.Combine(Dir, $"{Guid.NewGuid()}.md");

        var content = $"""
            ---
            asked: {DateTimeOffset.UtcNow:O}
            ---
            {query}
            """;

        await File.WriteAllTextAsync(path, content, ct);
        await events.PublishAsync(new StorageChanged($"Zapisano luke w wiedzy: {query}"), ct);
    }

    public async Task<IReadOnlyList<KnowledgeGap>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(Dir))
            return [];

        var gaps = new List<KnowledgeGap>();
        foreach (var file in Directory.EnumerateFiles(Dir, "*.md"))
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

    public async Task ResolveAsync(string path, CancellationToken ct = default)
    {
        if (File.Exists(path))
            File.Delete(path);

        await events.PublishAsync(new StorageChanged("Odrzucono luke w wiedzy"), ct);
    }
}
