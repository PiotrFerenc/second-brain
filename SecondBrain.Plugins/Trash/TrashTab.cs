using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Core;
using SecondBrain.Infrastructure;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Trash;

public record TrashItem(string Title, string OriginalFolder, string TrashPath, string RawContent);

// IShell brany leniwie: ta sama instancja jest handlerem zdarzen takze w CLI, gdzie powloki nie ma -
// konstruktor z IShell wywalalby EventBus przy pierwszym NoteTrashed z komendy delete-note.
public sealed partial class TrashTab(INoteStore noteStore, NotePipeline pipeline, IServiceProvider services)
    : ObservableObject, ITabContribution,
      IEventHandler<NoteTrashed>, IEventHandler<NoteRestored>, IEventHandler<NotePurged>
{
    public string Id => "trash";
    public string Title => "Kosz";
    public TabArea Placement => TabArea.SidebarToolbar;
    public int Order => 20;
    public KeyGesture? Shortcut => null;

    public Control CreateView() => new TrashView();

    public Task OnActivatedAsync(CancellationToken ct) => LoadAsync();

    public ObservableCollection<TrashItem> TrashItems { get; } = [];

    [ObservableProperty]
    public partial TrashItem? SelectedTrashItem { get; set; }

    [ObservableProperty]
    public partial bool HasTrash { get; set; }

    [RelayCommand]
    private async Task LoadAsync()
    {
        TrashItems.Clear();
        foreach (var t in await noteStore.ListTrashAsync())
            TrashItems.Add(new TrashItem(t.Note.Title, t.OriginalFolder, t.TrashPath, t.Note.RawContent));

        HasTrash = TrashItems.Count > 0;
    }

    [RelayCommand]
    private async Task RestoreFromTrashAsync(TrashItem? item)
    {
        item ??= SelectedTrashItem;
        if (item is null)
            return;

        await pipeline.RestoreAsync(item.TrashPath);

        await LoadAsync();
        // ponytail: host nie subskrybuje jeszcze NotesChanged - odswiezamy drzewo recznie.
        await services.GetRequiredService<IShell>().RefreshTreeAsync();
    }

    [RelayCommand]
    private async Task PurgeFromTrashAsync(TrashItem? item)
    {
        item ??= SelectedTrashItem;
        if (item is null)
            return;

        await pipeline.PurgeAsync(item.TrashPath);
        SelectedTrashItem = null;
        await LoadAsync();
    }

    public Task HandleAsync(NoteTrashed e, CancellationToken ct = default) => LoadAsync();
    public Task HandleAsync(NoteRestored e, CancellationToken ct = default) => LoadAsync();
    public Task HandleAsync(NotePurged e, CancellationToken ct = default) => LoadAsync();
}
