using Avalonia.Controls;
using SecondBrain.Core;

namespace SecondBrain.Plugins.Sdk;

// Powloka "bez okna" dla hostow bez UI (CLI PipelineTest): pluginy moga wstrzykiwac
// IShell/IEditorContext w konstruktorze, a ich handlery zdarzen dzialaja tez w CLI.
// Nawigacja i edytor to no-op; TopLevel rzuca - w CLI nie ma czego pokazac.
public sealed class NullShell : IShell
{
    public string? SelectedFolder => null;
    public NoteItem? SelectedNote => null;
    public NoteItem? SelectedSearchResult => null;
    public IReadOnlyList<string> Folders => [];
    public Func<Note, bool>? TreeFilter { get; set; }
    public TopLevel TopLevel => throw new InvalidOperationException("Brak okna - host bez UI.");
    public void ShowTab(string tabId) { }
    public void ShowNote(NoteItem note) { }
    public void Search(string query) { }
    public Task RefreshTreeAsync() => Task.CompletedTask;
}

public sealed class NullEditorContext : IEditorContext
{
    public string? Folder => null;
    public string Text { get; set; } = "";
    public string Status { set { } }
    public bool IsBusy { get; set; }
    public Task OpenQuickNoteAsync(string folder) => Task.CompletedTask;
}
