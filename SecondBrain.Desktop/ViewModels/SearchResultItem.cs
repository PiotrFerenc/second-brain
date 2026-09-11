namespace SecondBrain.Desktop.ViewModels;

public class SearchResultItem(Guid id, string title, string tags, float score, string rawContent, string filePath)
{
    public Guid Id { get; } = id;
    public string Title { get; } = title;
    public string Tags { get; } = tags;
    public float Score { get; } = score;
    public string RawContent { get; } = rawContent;
    public string FilePath { get; } = filePath;
}
