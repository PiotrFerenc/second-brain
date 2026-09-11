namespace SecondBrain.Desktop.ViewModels;

public class TrashItem(string title, string originalFolder, string trashPath, string rawContent)
{
    public string Title { get; } = title;
    public string OriginalFolder { get; } = originalFolder;
    public string TrashPath { get; } = trashPath;
    public string RawContent { get; } = rawContent;
}
