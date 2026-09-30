using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using SecondBrain.Core;
using SecondBrain.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Plugins.Sdk;
using Serilog;

namespace SecondBrain.Desktop.Plugins;

// IShell/IEditorContext dla pluginow: cienka delegacja do MainViewModel (singleton), zeby
// plugin nie znal VM hosta, a VM nie znal pluginow.
public sealed class ShellAdapter : IShell
{
    private readonly MainViewModel vm;
    private readonly IEnumerable<ITabContribution> tabs;

    public ShellAdapter(MainViewModel vm, IEnumerable<ITabContribution> tabs)
    {
        this.vm = vm;
        this.tabs = tabs;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedNote))
                SelectedNoteChanged?.Invoke();
        };
    }

    public event Action? SelectedNoteChanged;

    public string? SelectedFolder => vm.SelectedFolder;
    public NoteItem? SelectedNote => vm.SelectedNote;
    public NoteItem? SelectedSearchResult => SearchTab?.SelectedResult;

    private ISearchTab? SearchTab => tabs.FirstOrDefault(t => t.Id == "search") as ISearchTab;
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
        if (SearchTab is not { } search)
        {
            Log.Warning("Search: brak zakladki 'search' (plugin wylaczony?)");
            return;
        }
        ShowTab("search");
        search.Search(query);
    }

    // Lista rodzicow w edytorze to pochodna notatek folderu - odswiezana razem z drzewem.
    public async Task RefreshTreeAsync()
    {
        await vm.LoadTreeCommand.ExecuteAsync(null);
        await vm.LoadParentOptionsCommand.ExecuteAsync(null);
    }
}

public sealed class EditorContextAdapter : IEditorContext
{
    private readonly MainViewModel _vm;
    private readonly IServiceProvider _services;

    public EditorContextAdapter(MainViewModel vm, IServiceProvider services)
    {
        _vm = vm;
        _services = services;
        // Zmiany z VM przekazywane pod nazwami IEditorContext - okno szybkiej notatki binduje sie do adaptera.
        vm.PropertyChanged += (_, e) =>
        {
            var name = e.PropertyName switch
            {
                nameof(MainViewModel.NoteText) => nameof(Text),
                nameof(MainViewModel.EditorStatus) => nameof(Status),
                nameof(MainViewModel.IsBusy) => nameof(IsBusy),
                nameof(MainViewModel.SelectedFolder) => nameof(Folder),
                _ => null
            };
            if (name is not null)
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        };
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public string? Folder => _vm.SelectedFolder;
    public void SetFolder(string folder) => _vm.SelectedFolder = folder;

    public string Text
    {
        get => _vm.NoteText;
        set => _vm.NoteText = value;
    }

    public string Status
    {
        get => _vm.EditorStatus;
        set => _vm.EditorStatus = value;
    }

    public bool IsBusy
    {
        get => _vm.IsBusy;
        set => _vm.IsBusy = value;
    }

    public Task SaveAsync() => _vm.SaveNoteCommand.ExecuteAsync(null);

    // GetService, nie konstruktor: plugin quicknote sam zalezy od IEditorContext (cykl).
    public Task OpenQuickNoteAsync(string folder)
    {
        if (_services.GetService<IQuickNoteHost>() is { } host)
            return host.OpenAsync(folder);

        Log.Warning("OpenQuickNote: brak IQuickNoteHost (plugin quicknote wylaczony?)");
        return Task.CompletedTask;
    }
}
