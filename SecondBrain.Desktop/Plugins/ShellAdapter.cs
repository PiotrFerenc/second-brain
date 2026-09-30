using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using SecondBrain.Core;
using SecondBrain.Desktop.ViewModels;
using SecondBrain.Plugins.Sdk;
using Serilog;

namespace SecondBrain.Desktop.Plugins;

// IShell/IEditorContext dla pluginow: cienka delegacja do MainViewModel (singleton), zeby
// plugin nie znal VM hosta, a VM nie znal pluginow.
public sealed class ShellAdapter(MainViewModel vm, IEnumerable<ITabContribution> tabs) : IShell
{
    public string? SelectedFolder => vm.SelectedFolder;
    public NoteItem? SelectedNote => vm.SelectedNote;
    public NoteItem? SelectedSearchResult => vm.SelectedResult;
    public IReadOnlyList<string> Folders => vm.Tree.Where(t => t.IsFolder).Select(t => t.DisplayName).ToList();

    public Func<Note, bool>? TreeFilter
    {
        get => vm.TreeFilter;
        set => vm.TreeFilter = value;
    }

    public TopLevel TopLevel =>
        (Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)!.MainWindow!;

    public void ShowTab(string tabId)
    {
        var tab = tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab is null)
        {
            Log.Warning("ShowTab: brak zakladki '{TabId}' (plugin wylaczony?)", tabId);
            return;
        }
        _ = vm.ShowPluginTabCommand.ExecuteAsync(tab);
    }

    public void ShowNote(NoteItem note)
    {
        vm.SelectedNote = note;
        vm.SelectedTabIndex = MainViewModel.TabNote;
    }

    public void Search(string query)
    {
        vm.SearchQuery = query;
        vm.SelectedTabIndex = MainViewModel.TabSearch;
        _ = vm.SearchCommand.ExecuteAsync(null);
    }

    public Task RefreshTreeAsync() => vm.LoadTreeCommand.ExecuteAsync(null);
}

public sealed class EditorContextAdapter(MainViewModel vm) : IEditorContext
{
    public string? Folder => vm.SelectedFolder;

    public string Text
    {
        get => vm.NoteText;
        set => vm.NoteText = value;
    }

    public string Status
    {
        set => vm.EditorStatus = value;
    }

    public bool IsBusy
    {
        get => vm.IsBusy;
        set => vm.IsBusy = value;
    }

    public Task OpenQuickNoteAsync(string folder)
    {
        ((App)Application.Current!).OpenQuickNote(folder);
        return Task.CompletedTask;
    }
}
