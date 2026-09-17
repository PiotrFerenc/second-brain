using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

// Czyste KNN z lokalnego indeksu wektorowego potrafi pominac notatke, gdy embedder nie daje realnego sygnalu
// semantycznego (patrz MockEmbedder - losowy wektor z hasha calego tekstu, top-K to wtedy
// losowa probka folderu) albo gdy zapytanie to dosowna fraza/nazwa wlasna wyladowana daleko
// w przestrzeni wektorowej mimo realnego embeddingu. Dlatego obok top-K z wektorow dokladamy
// leksykalny skan calego folderu (te same slowa-kryteria co MockReranker.CountOverlap) -
// polaczenie kandydatow, nie zastapienie KNN; ostateczne sortowanie i tak robi reranker.
public static class HybridNoteSearch
{
    public static async Task<List<(string Folder, ScoredNote Scored)>> SearchFoldersAsync(
        IVectorIndex vectorIndex,
        INoteStore noteStore,
        IReadOnlyList<string> folders,
        string query,
        float[] queryVector,
        ulong vectorLimit,
        CancellationToken ct = default)
    {
        var queryWords = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var candidates = new List<(string Folder, ScoredNote Scored)>();

        foreach (var folder in folders)
        {
            var seen = new HashSet<Guid>();

            foreach (var hit in await vectorIndex.SearchAsync(folder, queryVector, vectorLimit, ct))
            {
                if (seen.Add(hit.Note.Id))
                    candidates.Add((folder, hit));
            }

            if (queryWords.Length == 0)
                continue;

            foreach (var note in await noteStore.ListAsync(folder, ct))
            {
                if (!seen.Add(note.Id))
                    continue;

                var haystack = $"{note.Title} {note.CompressedContent}".ToLowerInvariant();
                if (queryWords.Any(haystack.Contains))
                    candidates.Add((folder, new ScoredNote(note, 0f)));
            }
        }

        return candidates;
    }
}
