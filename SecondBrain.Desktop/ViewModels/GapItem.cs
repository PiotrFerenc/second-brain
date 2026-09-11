namespace SecondBrain.Desktop.ViewModels;

public class GapItem(string query, DateTimeOffset askedAt, string path)
{
    public string Query { get; } = query;
    public DateTimeOffset AskedAt { get; } = askedAt;
    public string Path { get; } = path;
}
