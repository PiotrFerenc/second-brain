namespace SecondBrain.Desktop.ViewModels;

public class SearchResultItem(Guid id, string title, string[] tagList, float score, string rawContent, string filePath, Guid? parentId, bool pinned, string folder)
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
}
