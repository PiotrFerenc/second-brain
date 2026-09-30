using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using SecondBrain.Core;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.History;

// Zakladka "Historia" (cale repo notatek z gita - lista commitow + diff, jak SourceTree).
// Commity ladowane przy kazdym wejsciu w zakladke (OnActivatedAsync), nie na starcie.
public sealed partial class HistoryTab(GitRepository git) : ObservableObject, ITabContribution
{
    public string Id => "history";
    public string Title => "Historia";
    public TabArea Placement => TabArea.SidebarToolbar;
    public int Order => 60;
    public KeyGesture? Shortcut => null;

    public Control CreateView() => new HistoryView();

    public ObservableCollection<CommitEntry> Commits { get; } = [];

    [ObservableProperty]
    public partial CommitEntry? SelectedCommit { get; set; }

    [ObservableProperty]
    public partial bool HasCommits { get; set; }

    public ObservableCollection<DiffFile> CommitDiffFiles { get; } = [];

    [ObservableProperty]
    public partial bool HasCommitDiff { get; set; }

    public async Task OnActivatedAsync(CancellationToken ct)
    {
        Commits.Clear();
        foreach (var c in await git.ListCommitsAsync(ct: ct))
            Commits.Add(c);

        HasCommits = Commits.Count > 0;
    }

    partial void OnSelectedCommitChanged(CommitEntry? value) => _ = LoadCommitDiffAsync();

    private async Task LoadCommitDiffAsync()
    {
        CommitDiffFiles.Clear();
        if (SelectedCommit is not null)
            foreach (var file in await git.GetCommitDiffAsync(SelectedCommit.Hash))
                CommitDiffFiles.Add(file);

        HasCommitDiff = CommitDiffFiles.Count > 0;
    }
}
