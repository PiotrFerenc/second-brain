using Avalonia;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Core;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Timeline;

public record TimelineGroup(string Label, IReadOnlyList<NoteItem> Items);

// Grupy budowane z magazynu (nie z drzewa hosta): przy otwarciu zakladki i po kazdej
// mutacji notatek (NotesChanged) - plugin nie wie, co dokladnie sie zmienilo, wiec przelicza calosc.
// IShell brany leniwie: handler NotesChanged dziala tez w CLI (PipelineTest), gdzie nie ma powloki -
// wstrzykniecie IShell w konstruktorze wywalaloby tam kazdy `add`.
public sealed partial class TimelineTab(IVectorIndex vectorIndex, INoteStore noteStore, IServiceProvider services)
    : ObservableObject, ITabContribution, IEventHandler<NotesChanged>
{
    public string Id => "timeline";
    public string Title => "Oś czasu";
    public TabArea Placement => TabArea.SidebarToolbar;
    public int Order => 50;
    public KeyGesture? Shortcut => null;

    public ObservableCollection<TimelineGroup> Groups { get; } = [];

    [ObservableProperty]
    public partial bool HasTimeline { get; set; }

    public Control CreateView() => new TimelineView();

    public Task OnActivatedAsync(CancellationToken ct) => RebuildAsync(ct);

    public Task HandleAsync(NotesChanged e, CancellationToken ct = default) => RebuildAsync(ct);

    private async Task RebuildAsync(CancellationToken ct)
    {
        var allNotes = new List<NoteItem>();
        foreach (var folder in await vectorIndex.ListFoldersAsync(ct))
            foreach (var n in await noteStore.ListAsync(folder, ct))
                allNotes.Add(new NoteItem(n.Id, n.Title, n.Tags, 0f, n.RawContent, n.FilePath, n.ParentId, n.Pinned, folder, n.CreatedAt));

        var groups = allNotes
            .OrderByDescending(n => n.CreatedAt)
            .GroupBy(n => n.CreatedAt.Date)
            .Select(g => new TimelineGroup(g.Key.ToString("d MMMM yyyy", new CultureInfo("pl-PL")), g.ToList()))
            .ToList();

        // NotesChanged moze przyjsc spoza watku UI (agent) - kolekcja bindowana wymaga UI.
        // W CLI nie ma petli Avalonii: InvokeAsync nigdy by sie nie wykonal i `add` wisialby
        // w nieskonczonosc, wiec bez aplikacji aktualizujemy wprost.
        void Apply()
        {
            Groups.Clear();
            foreach (var g in groups)
                Groups.Add(g);
            HasTimeline = Groups.Count > 0;
        }

        if (Application.Current is null)
            Apply();
        else
            await Dispatcher.UIThread.InvokeAsync(Apply);
    }

    [RelayCommand]
    private void OpenNote(NoteItem? item)
    {
        if (item is not null)
            services.GetRequiredService<IShell>().ShowNote(item);
    }
}
