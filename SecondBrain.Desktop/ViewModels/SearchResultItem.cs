namespace SecondBrain.Desktop.ViewModels;

public class SearchResultItem(string title, string tags, float score, string rawContent)
{
    public string Title { get; } = title;
    public string Tags { get; } = tags;
    public float Score { get; } = score;
    public string RawContent { get; } = rawContent;
}
