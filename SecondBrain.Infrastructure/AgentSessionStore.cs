using System.Text.Json;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

// ponytail: JSON przez System.Text.Json zamiast recznego front matter jak w FileNoteStore -
// sesja ma zagniezdzona liste wiadomosci, wiec plaski parser front matter (celowy wybor dla
// notatek) tu tylko by przeszkadzal. System.Text.Json jest juz zaleznoscia wszedzie indziej
// w tym projekcie (Compression/AnswerSynthesis/...), wiec to nie nowa biblioteka.
public class FileAgentSessionStore(NotesRoot notesRoot) : IAgentSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _root = notesRoot.Path;

    private string SessionsDir => Path.Combine(_root, ".agent-sessions");

    public async Task<IReadOnlyList<AgentSession>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(SessionsDir))
            return [];

        var sessions = new List<AgentSession>();
        foreach (var file in Directory.EnumerateFiles(SessionsDir, "*.json"))
        {
            await using var stream = File.OpenRead(file);
            var session = await JsonSerializer.DeserializeAsync<AgentSession>(stream, JsonOptions, ct);
            if (session is not null)
                sessions.Add(session);
        }

        return sessions.OrderByDescending(s => s.UpdatedAt).ToList();
    }

    public async Task SaveAsync(AgentSession session, CancellationToken ct = default)
    {
        Directory.CreateDirectory(SessionsDir);
        var path = Path.Combine(SessionsDir, $"{session.Id}.json");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, session, JsonOptions, ct);
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var path = Path.Combine(SessionsDir, $"{id}.json");
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }
}
