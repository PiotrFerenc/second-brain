using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

public record SearchHit(string Folder, Note Note, float Score);

// Wyszukiwanie jako serwis: embedding zapytania -> kandydaci hybrydowi (wektor + leksykalnie,
// patrz HybridNoteSearch) po wskazanych folderach (null = wszystkie) -> reranker -> pelne
// notatki doczytane z dysku (payload indeksu moze byc nieswiezy). Uzywane przez UI, agenta,
// CLI i domykanie luk - wczesniej kazde z tych miejsc mialo wlasna kopie tej sekwencji.
public sealed class NoteSearch(IVectorIndex vectorIndex, INoteStore noteStore, IEmbedder embedder, IReranker reranker)
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, IReadOnlyList<string>? folders = null, ulong vectorLimit = 10, CancellationToken ct = default)
    {
        var queryVector = await embedder.EmbedAsync(query, ct);
        folders ??= await vectorIndex.ListFoldersAsync(ct);

        var candidates = await HybridNoteSearch.SearchFoldersAsync(vectorIndex, noteStore, folders, query, queryVector, vectorLimit, ct);
        var folderById = candidates.ToDictionary(c => c.Scored.Note.Id, c => c.Folder);
        var reranked = await reranker.RerankAsync(query, candidates.Select(c => c.Scored).ToList(), ct);

        var hits = new List<SearchHit>();
        foreach (var r in reranked)
        {
            var note = r.Note;
            if (!string.IsNullOrEmpty(note.FilePath) && File.Exists(note.FilePath))
                note = await noteStore.LoadAsync(note.FilePath, ct);
            hits.Add(new SearchHit(folderById.GetValueOrDefault(r.Note.Id, ""), note, r.Score));
        }

        return hits;
    }
}
