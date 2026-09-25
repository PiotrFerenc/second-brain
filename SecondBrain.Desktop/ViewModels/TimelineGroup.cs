namespace SecondBrain.Desktop.ViewModels;

public record TimelineGroup(string Label, IReadOnlyList<SearchResultItem> Items);
