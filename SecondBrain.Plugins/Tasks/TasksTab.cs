using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Core;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Tasks;

public class TaskItem(TaskEntry entry, DateOnly today)
{
    public TaskEntry Entry { get; } = entry;
    public string Text => Entry.Text;
    public string Source => Entry.NoteTitle;
    public bool IsOverdue { get; } = TaskScanner.IsOverdue(entry, today);
    public string DueText => Entry.Due is { } d ? $"termin {d:yyyy-MM-dd}" + (IsOverdue ? " - po terminie!" : "") : "";
}

// Zakladka "Zadania (N)": przelicza sie po kazdym NotesChanged i przy aktywacji (jak Os czasu -
// plugin nie wie, co sie zmienilo). IShell brany leniwie, bo handler dziala tez w CLI.
public sealed partial class TasksTab(IVectorIndex vectorIndex, INoteStore noteStore, IServiceProvider services)
    : ObservableObject, ITabContribution, IEventHandler<NotesChanged>
{
    public string Id => "tasks";
    public string Title => _overdue > 0 ? $"Zadania ({Tasks.Count}, {_overdue} po terminie)" : $"Zadania ({Tasks.Count})";
    public TabArea Placement => TabArea.SidebarToolbar;
    public int Order => 35;
    public KeyGesture? Shortcut => null;

    private int _overdue;

    public ObservableCollection<TaskItem> Tasks { get; } = [];

    [ObservableProperty]
    public partial bool HasTasks { get; set; }

    public Control CreateView() => new TasksView();

    public Task OnActivatedAsync(CancellationToken ct) => LoadAsync(ct);

    public Task HandleAsync(NotesChanged e, CancellationToken ct = default) => LoadAsync(ct);

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var items = TaskScanner.OpenSorted(await TaskScanner.ScanAllAsync(vectorIndex, noteStore, ct))
            .Select(t => new TaskItem(t, today)).ToList();

        // Bez petli Avalonii (CLI) InvokeAsync wisialby w nieskonczonosc - aktualizacja wprost.
        void Apply()
        {
            Tasks.Clear();
            foreach (var t in items)
                Tasks.Add(t);
            _overdue = items.Count(t => t.IsOverdue);
            HasTasks = items.Count > 0;
            OnPropertyChanged(nameof(Title));
        }

        if (Application.Current is null)
            Apply();
        else
            await Dispatcher.UIThread.InvokeAsync(Apply);
    }

    [RelayCommand]
    private async Task OpenNoteAsync(TaskItem? item)
    {
        if (item is null)
            return;

        var note = (await noteStore.ListAsync(item.Entry.Folder)).FirstOrDefault(n => n.Id == item.Entry.NoteId);
        if (note is not null)
            services.GetRequiredService<IShell>().ShowNote(new NoteItem(note.Id, note.Title, note.Tags, 0f, note.RawContent, note.FilePath, note.ParentId, note.Pinned, item.Entry.Folder, note.CreatedAt));
    }
}
