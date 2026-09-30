namespace SecondBrain.Plugins.Sdk;

// DTO notatki do bindowania w UI (drzewo, szukaj, os czasu, kosz) - wspolne dla hosta i pluginow.
public class NoteItem(Guid id, string title, string[] tagList, float score, string rawContent, string filePath, Guid? parentId, bool pinned, string folder, DateTimeOffset createdAt = default)
{
    public Guid Id { get; } = id;
    public string Title { get; } = title;
    public string[] TagList { get; } = tagList;
    public string Tags { get; } = string.Join(", ", tagList);
    public float Score { get; } = score;
    public string RawContent { get; } = rawContent;
    public string FilePath { get; } = filePath;
    public Guid? ParentId { get; } = parentId;
    public bool Pinned { get; } = pinned;
    public string Folder { get; } = folder;
    public DateTimeOffset CreatedAt { get; } = createdAt;
}
