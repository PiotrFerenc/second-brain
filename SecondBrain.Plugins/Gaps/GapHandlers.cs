using SecondBrain.Core;
using SecondBrain.Infrastructure;

namespace SecondBrain.Plugins.Gaps;

// RAG jawnie powiedzial "notatki tego nie zawieraja" - pytanie zostaje jako luka w wiedzy.
public sealed class GapLogOnSearch(INoteStore noteStore) : IEventHandler<SearchCompleted>
{
    public Task HandleAsync(SearchCompleted e, CancellationToken ct = default) =>
        e.Answered ? Task.CompletedTask : noteStore.LogGapAsync(e.Query, ct);
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

// Po kazdej nowej notatce probujemy ponownie odpowiedziec na otwarte luki - jesli RAG teraz
// zwroci Answered:true, luka zamyka sie sama, bez akcji uzytkownika.
public sealed class GapAutoCloser(INoteStore noteStore, NoteSearch noteSearch, IAnswerSynthesizer answerSynthesizer)
{
    public async Task<int> TryCloseMatchingGapsAsync(CancellationToken ct = default)
    {
        var gaps = await noteStore.ListGapsAsync(ct);
        if (gaps.Count == 0)
            return 0;

        var closed = 0;
        foreach (var gap in gaps)
        {
            var notes = (await noteSearch.SearchAsync(gap.Query, folders: null, vectorLimit: 10, ct)).Take(5).Select(h => h.Note).ToList();
            if (notes.Count == 0)
                continue;

            var answer = await answerSynthesizer.SynthesizeAsync(gap.Query, notes, ct);
            if (!answer.Answered)
                continue;

            await noteStore.ResolveGapAsync(gap.Path, ct);
            closed++;
        }

        return closed;
    }
}
