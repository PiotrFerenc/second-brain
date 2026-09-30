using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecondBrain.Core;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Gaps;

public class GapItem(string query, DateTimeOffset askedAt, string path)
{
    public string Query { get; } = query;
    public DateTimeOffset AskedAt { get; } = askedAt;
    public string Path { get; } = path;
}

// Zakladka "Luki (N)". Lista przeladowuje sie po kazdym StorageChanged (wlasne handlery,
// agent, CLI - wszystko przechodzi przez GapStore, ktory to publikuje) i przy aktywacji.
// IShell opcjonalny: w CLI nie ma powloki, a handlery zdarzen i tak tworza te klase.
public sealed partial class GapsTab(GapStore gaps, IShell? shell = null) : ObservableObject, ITabContribution, IEventHandler<StorageChanged>
{
    public string Id => "gaps";
    public string Title => $"Luki ({Gaps.Count})";
    public TabArea Placement => TabArea.SidebarToolbar;
    public int Order => 30;
    public KeyGesture? Shortcut => null;

    public Control CreateView() => new GapsView();

    public Task OnActivatedAsync(CancellationToken ct) => LoadGapsAsync();

    public Task HandleAsync(StorageChanged e, CancellationToken ct = default) => LoadGapsAsync();

    public ObservableCollection<GapItem> Gaps { get; } = [];

    [ObservableProperty]
    public partial GapItem? SelectedGap { get; set; }

    [ObservableProperty]
    public partial bool HasGaps { get; set; }

    [RelayCommand]
    private async Task LoadGapsAsync()
    {
        Gaps.Clear();
        foreach (var g in await gaps.ListAsync())
            Gaps.Add(new GapItem(g.Query, g.AskedAt, g.Path));

        HasGaps = Gaps.Count > 0;
        OnPropertyChanged(nameof(Title));
    }

    [RelayCommand]
    private async Task ResolveGapAsync(GapItem? item)
    {
        item ??= SelectedGap;
        if (item is null)
            return;

        await gaps.ResolveAsync(item.Path);
        SelectedGap = null;
        await LoadGapsAsync();
    }

    [RelayCommand]
    private void RetryGapSearch(GapItem? item)
    {
        item ??= SelectedGap;
        if (item is null)
            return;

        shell?.Search(item.Query);
    }
}
