using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

// Po kazdej nowej notatce probujemy ponownie odpowiedziec na otwarte "Luki w wiedzy" -
// jesli RAG teraz zwroci Answered:true, luka zamyka sie sama, bez akcji uzytkownika.
public class GapAutoCloser(INoteStore noteStore, NoteSearch noteSearch, IAnswerSynthesizer answerSynthesizer)
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
