using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

// Po kazdej nowej notatce probujemy ponownie odpowiedziec na otwarte "Luki w wiedzy" -
// jesli RAG teraz zwroci Answered:true, luka zamyka sie sama, bez akcji uzytkownika.
public class GapAutoCloser(
    INoteStore noteStore,
    IVectorIndex vectorIndex,
    IEmbedder embedder,
    IReranker reranker,
    IAnswerSynthesizer answerSynthesizer)
{
    public async Task<int> TryCloseMatchingGapsAsync(CancellationToken ct = default)
    {
        var gaps = await noteStore.ListGapsAsync(ct);
        if (gaps.Count == 0)
            return 0;

        var closed = 0;
        foreach (var gap in gaps)
        {
            var queryVector = await embedder.EmbedAsync(gap.Query, ct);
            var folders = await vectorIndex.ListFoldersAsync(ct);

            var candidates = new List<ScoredNote>();
            foreach (var folder in folders)
                candidates.AddRange(await vectorIndex.SearchAsync(folder, queryVector, limit: 10, ct: ct));

            var reranked = await reranker.RerankAsync(gap.Query, candidates, ct);

            var notes = new List<Note>();
            foreach (var r in reranked.Take(5))
            {
                var note = r.Note;
                if (!string.IsNullOrEmpty(note.FilePath) && File.Exists(note.FilePath))
                    note = await noteStore.LoadAsync(note.FilePath, ct);
                notes.Add(note);
            }

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
