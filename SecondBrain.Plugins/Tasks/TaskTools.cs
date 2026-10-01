using System.Text.Json;
using SecondBrain.Core;
using SecondBrain.Infrastructure.AgentTools;

namespace SecondBrain.Plugins.Tasks;

// Narzedzie agenta pluginu zadan - rejestrowane w TasksPlugin, wylaczony plugin go nie ma.
public sealed class ListTasksTool(IVectorIndex index, INoteStore store) : AgentTool
{
    public override string Name => "list_tasks";
    public override string Description => "Wylistuj otwarte zadania ('- [ ] ...') ze wszystkich notatek, od najpilniejszych. Termin to data RRRR-MM-DD w tekscie zadania.";
    protected override string Parameters => """{"type":"object","properties":{"includeDone":{"type":"boolean","description":"Dolacz tez wykonane zadania ('- [x]')."}}}""";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var all = await TaskScanner.ScanAllAsync(index, store, ct);
        var includeDone = args.TryGetProperty("includeDone", out var v) && v.ValueKind == JsonValueKind.True;
        var tasks = includeDone ? all : TaskScanner.OpenSorted(all);
        return JsonSerializer.Serialize(tasks.Select(t => new
        {
            t.Text, due = t.Due?.ToString("yyyy-MM-dd"), t.Done, note = t.NoteTitle, noteId = t.NoteId, folder = t.Folder
        }));
    }
}
