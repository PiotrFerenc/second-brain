using System.Text.Json.Serialization;

namespace SecondBrain.Core;

public record CompressionResult(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("content")] string CompressedContent,
    [property: JsonPropertyName("tags")] string[] Tags);

public record TrashedNote(Note Note, string OriginalFolder, string TrashPath);

public interface ICompressor
{
    Task<CompressionResult> CompressAsync(string rawText, CancellationToken ct = default);
}

public interface IEmbedder
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
}

public interface IReranker
{
    Task<IReadOnlyList<ScoredNote>> RerankAsync(string query, IReadOnlyList<ScoredNote> candidates, CancellationToken ct = default);
}

// Osobny krok od wyszukiwania: bierze juz-znalezione notatki i syntetyzuje z nich
// bezposrednia odpowiedz na pytanie. Wyszukiwanie samo w sobie dziala bez tego kroku.
public interface IAnswerSynthesizer
{
    Task<string> SynthesizeAsync(string query, IReadOnlyList<Note> notes, CancellationToken ct = default);
}

public interface INoteStore
{
    Task<string> SaveAsync(string folder, Note note, CancellationToken ct = default);
    Task<Note> LoadAsync(string filePath, CancellationToken ct = default);
    Task<IReadOnlyList<Note>> ListAsync(string folder, CancellationToken ct = default);
    Task DeleteFolderAsync(string folder, CancellationToken ct = default);

    // Kosz: usuniecie notatki przenosi plik do .trash zamiast go kasowac.
    Task<string> MoveToTrashAsync(string folder, string filePath, CancellationToken ct = default);
    Task<IReadOnlyList<TrashedNote>> ListTrashAsync(CancellationToken ct = default);
    Task<TrashedNote> RestoreFromTrashAsync(string trashPath, CancellationToken ct = default);
    Task PurgeTrashAsync(string trashPath, CancellationToken ct = default);
}

// Folder = kolekcja w bazie wektorowej. Jedna implementacja (Qdrant) na razie,
// interfejs istnieje żeby Core i Desktop nie zależały od konkretnego klienta bazy.
public interface IVectorIndex
{
    Task<IReadOnlyList<string>> ListFoldersAsync(CancellationToken ct = default);
    Task<bool> CreateFolderAsync(string name, CancellationToken ct = default);
    Task DeleteFolderAsync(string name, CancellationToken ct = default);
    Task UpsertAsync(string folder, Note note, float[] vector, CancellationToken ct = default);
    Task DeleteNoteAsync(string folder, Guid noteId, CancellationToken ct = default);
    Task<IReadOnlyList<ScoredNote>> SearchAsync(string folder, float[] vector, ulong limit, CancellationToken ct = default);
}
