using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

// Czysto odczytowy skan podobienstwa wektorowego miedzy folderami - znajduje notatki
// ktore sa niemal identyczne ale zyja w dwoch roznych folderach (kandydaci do recznego
// scalenia przez usera/agenta - ten skan nic sam nie kasuje/scala).
// ponytail: O(notatki x foldery) wywolan SearchAsync, wystarczajace przy skali osobistej
// bazy wiedzy (dziesiatki-setki notatek); batchowanie dopiero gdy realnie zwolni.
public class DuplicateScanner(IVectorIndex vectorIndex, IEmbedder embedder, INoteStore noteStore)
{
    public async Task<IReadOnlyList<(Note A, Note B, float Similarity)>> FindCrossFolderDuplicatesAsync(
        float threshold = 0.92f, CancellationToken ct = default)
    {
        var folders = await vectorIndex.ListFoldersAsync(ct);
        var seenPairs = new HashSet<(Guid, Guid)>();
        var results = new List<(Note, Note, float)>();

        foreach (var folder in folders)
        {
            var notes = await noteStore.ListAsync(folder, ct);
            foreach (var note in notes)
            {
                // Wlasny wektor notatki nie jest gdzies trzymany do odczytu - ponownie
                // embedujemy jej skompresowana tresc, spojnie z tym jak reszta kodu
                // (search/ask) tez zawsze liczy wektor zapytania na biezaco.
                var vector = await embedder.EmbedAsync(note.CompressedContent, ct);

                foreach (var otherFolder in folders)
                {
                    if (otherFolder == folder)
                        continue;

                    var hits = await vectorIndex.SearchAsync(otherFolder, vector, limit: 3, ct: ct);
                    foreach (var hit in hits.Where(h => h.Score >= threshold))
                    {
                        var pairKey = note.Id.CompareTo(hit.Note.Id) < 0
                            ? (note.Id, hit.Note.Id)
                            : (hit.Note.Id, note.Id);

                        if (seenPairs.Add(pairKey))
                            results.Add((note, hit.Note, hit.Score));
                    }
                }
            }
        }

        return results;
    }
}
