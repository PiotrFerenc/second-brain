using System.Text.Json;
using Microsoft.Extensions.Options;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

// Folder = jeden plik JSON (tablica wpisow) pod NotesRootPath/.vectors. Male ilosci
// notatek (dziesiatki-setki) nie uzasadniaja osobnego serwera wektorowego jak Qdrant -
// wyszukiwanie to brute-force cosine similarity po wczytaniu calego pliku do pamieci.
// ponytail: jeden lock na caly indeks (nie per-folder) - prostsze, a zapisy notatek
// i tak sa rzadkie wzgledem odczytow.
public class FileVectorIndex(IOptions<StorageOptions> options) : IVectorIndex
{
    private readonly string _dir = Path.Combine(
        string.IsNullOrWhiteSpace(options.Value.NotesRootPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SecondBrain", "notes")
            : options.Value.NotesRootPath,
        ".vectors");

    private readonly SemaphoreSlim _lock = new(1, 1);

    public Task<IReadOnlyList<string>> ListFoldersAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_dir))
            return Task.FromResult<IReadOnlyList<string>>([]);

        IReadOnlyList<string> folders = Directory.EnumerateFiles(_dir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Select(n => n!)
            .ToList();

        return Task.FromResult(folders);
    }

    public async Task<bool> CreateFolderAsync(string name, CancellationToken ct = default)
    {
        var path = FolderPath(name);
        if (File.Exists(path))
            return false;

        Directory.CreateDirectory(_dir);
        await SaveAsync(path, [], ct);
        return true;
    }

    public Task DeleteFolderAsync(string name, CancellationToken ct = default)
    {
        var path = FolderPath(name);
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    public async Task UpsertAsync(string folder, Note note, float[] vector, CancellationToken ct = default)
    {
        var path = FolderPath(folder);
        await _lock.WaitAsync(ct);
        try
        {
            var entries = await LoadAsync(path, ct);
            entries.RemoveAll(e => e.Id == note.Id);
            entries.Add(new VectorEntry(note.Id, vector, note.Title, note.CompressedContent, note.Tags,
                note.FilePath, note.CreatedAt, note.UpdatedAt));
            await SaveAsync(path, entries, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteNoteAsync(string folder, Guid noteId, CancellationToken ct = default)
    {
        var path = FolderPath(folder);
        await _lock.WaitAsync(ct);
        try
        {
            var entries = await LoadAsync(path, ct);
            entries.RemoveAll(e => e.Id == noteId);
            await SaveAsync(path, entries, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<ScoredNote>> SearchAsync(string folder, float[] vector, ulong limit, CancellationToken ct = default)
    {
        var entries = await LoadAsync(FolderPath(folder), ct);

        return entries
            .Select(e => new ScoredNote(
                new Note(e.Id, e.Title, e.CompressedContent, e.CompressedContent, e.Tags, e.CreatedAt, e.UpdatedAt, e.FilePath),
                CosineSimilarity(vector, e.Vector)))
            .OrderByDescending(s => s.Score)
            .Take((int)limit)
            .ToList();
    }

    private string FolderPath(string folder) => Path.Combine(_dir, $"{folder}.json");

    private static async Task<List<VectorEntry>> LoadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            return [];

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<VectorEntry>>(stream, cancellationToken: ct) ?? [];
    }

    private static async Task SaveAsync(string path, List<VectorEntry> entries, CancellationToken ct)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, entries, cancellationToken: ct);
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        float dot = 0f, normA = 0f, normB = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0)
            return 0f;

        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }

    private record VectorEntry(Guid Id, float[] Vector, string Title, string CompressedContent, string[] Tags,
        string FilePath, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
