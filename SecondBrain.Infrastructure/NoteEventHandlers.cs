using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

// Reakcje na cykl zycia notatki, wczesniej wplecione w kazda kopie pipeline'u. Kolejnosc
// rejestracji w DI = kolejnosc wykonania: sprzecznosc -> domykanie luk (slownik: plugin glossary).

// Tylko pojedyncze dodanie (nie import: N linii = N wywolan LLM, 2N byloby za drogie).
public sealed class ConflictOnNoteAdded(IConflictDetector conflictDetector, INoteStore noteStore) : IEventHandler<NoteAdded>
{
    public async Task HandleAsync(NoteAdded e, CancellationToken ct = default)
    {
        if (e.FromImport || e.Related.Count == 0)
            return;

        var conflict = await conflictDetector.DetectAsync(e.Note.CompressedContent, e.Related, ct);
        if (!conflict.HasConflict)
            return;

        e.Notices.Add($"UWAGA - mozliwa sprzecznosc z \"{conflict.ConflictingTitle}\": {conflict.Explanation}");

        // Pierwsza sprzecznosc dla tematu: dopisz tez PIERWOTNA wersje, zeby historia
        // od razu miala obie strony.
        if ((await noteStore.ListFactHistoryAsync(conflict.ConflictingTitle!, ct)).Count == 0)
        {
            var original = e.Related.First(n => n.Title == conflict.ConflictingTitle);
            await noteStore.RecordFactVersionAsync(conflict.ConflictingTitle!, original.CompressedContent, original.Title, ct);
        }
        await noteStore.RecordFactVersionAsync(conflict.ConflictingTitle!, e.Note.CompressedContent, e.Result.Title, ct);
    }
}

// Po pojedynczym dodaniu i raz po calym imporcie (nie per linia).
public sealed class GapAutoCloseOnNoteAdded(GapAutoCloser gapAutoCloser) : IEventHandler<NoteAdded>, IEventHandler<ImportCompleted>
{
    public Task HandleAsync(NoteAdded e, CancellationToken ct = default) => e.FromImport ? Task.CompletedTask : CloseAsync(e.Notices, ct);
    public Task HandleAsync(ImportCompleted e, CancellationToken ct = default) => CloseAsync(e.Notices, ct);

    private async Task CloseAsync(ICollection<string> notices, CancellationToken ct)
    {
        var closed = await gapAutoCloser.TryCloseMatchingGapsAsync(ct);
        if (closed > 0)
            notices.Add($"Zamknieto {closed} luk(i) w wiedzy.");
    }
}

// RAG jawnie powiedzial "notatki tego nie zawieraja" - pytanie zostaje jako luka w wiedzy.
public sealed class GapLogOnSearch(INoteStore noteStore) : IEventHandler<SearchCompleted>
{
    public Task HandleAsync(SearchCompleted e, CancellationToken ct = default) =>
        e.Answered ? Task.CompletedTask : noteStore.LogGapAsync(e.Query, ct);
}

// Cichy backup do gita po kazdej zmianie na dysku (patrz GitRepository.ScheduleCommit).
public sealed class GitCommitOnChange(GitRepository git) : IEventHandler<StorageChanged>
{
    public Task HandleAsync(StorageChanged e, CancellationToken ct = default)
    {
        git.ScheduleCommit(e.Message);
        return Task.CompletedTask;
    }
}
