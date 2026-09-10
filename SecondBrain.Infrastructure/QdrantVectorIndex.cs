using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

// Kolekcja Qdrant = "folder" notatek. Kazda kolekcja ma ten sam schemat wektora,
// wiec tworzenie nowego folderu to po prostu CreateCollectionAsync pod nowa nazwa.
public class QdrantVectorIndex : IVectorIndex
{
    private readonly QdrantClient _client;
    private readonly QdrantOptions _options;

    public QdrantVectorIndex(IOptions<QdrantOptions> options)
    {
        _options = options.Value;
        _client = new QdrantClient(_options.Host, _options.Port);
    }

    public async Task<IReadOnlyList<string>> ListFoldersAsync(CancellationToken ct = default)
    {
        var collections = await _client.ListCollectionsAsync(ct);
        return collections.ToList();
    }

    public async Task<bool> CreateFolderAsync(string name, CancellationToken ct = default)
    {
        if (await _client.CollectionExistsAsync(name, ct))
            return false;

        await _client.CreateCollectionAsync(name, new VectorParams
        {
            Size = _options.VectorSize,
            Distance = Enum.Parse<Distance>(_options.Distance)
        }, cancellationToken: ct);

        return true;
    }

    public async Task UpsertAsync(string folder, Note note, float[] vector, CancellationToken ct = default)
    {
        var point = new PointStruct
        {
            Id = note.Id,
            Vectors = vector,
            Payload =
            {
                ["title"] = note.Title,
                ["compressed_content"] = note.CompressedContent,
                ["tags"] = string.Join(",", note.Tags),
                ["file_path"] = note.FilePath,
                ["created_at"] = note.CreatedAt.ToString("O"),
                ["updated_at"] = note.UpdatedAt.ToString("O")
            }
        };

        await _client.UpsertAsync(folder, [point], cancellationToken: ct);
    }

    public async Task<IReadOnlyList<ScoredNote>> SearchAsync(string folder, float[] vector, ulong limit, CancellationToken ct = default)
    {
        var hits = await _client.QueryAsync(folder, query: vector, limit: limit, cancellationToken: ct);

        return hits.Select(h =>
        {
            var payload = h.Payload;
            var createdAt = payload.TryGetValue("created_at", out var c) ? DateTimeOffset.Parse(c.StringValue) : DateTimeOffset.MinValue;
            var updatedAt = payload.TryGetValue("updated_at", out var u) ? DateTimeOffset.Parse(u.StringValue) : DateTimeOffset.MinValue;
            var filePath = payload.TryGetValue("file_path", out var f) ? f.StringValue : "";

            var note = new Note(
                Guid.Parse(h.Id.Uuid),
                payload["title"].StringValue,
                payload["compressed_content"].StringValue,
                payload["compressed_content"].StringValue,
                payload["tags"].StringValue.Split(',', StringSplitOptions.RemoveEmptyEntries),
                createdAt,
                updatedAt,
                filePath);

            return new ScoredNote(note, h.Score);
        }).ToList();
    }
}
