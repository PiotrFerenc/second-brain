using Avalonia.Controls;
using SecondBrain.Core;

namespace SecondBrain.Plugins.Sdk;

// Stan powloki i nawigacja - jedyne, co plugin wie o glownym oknie.
public interface IShell
{
    string? SelectedFolder { get; }
    NoteItem? SelectedNote { get; }
    event Action? SelectedNoteChanged;         // po zmianie SelectedNote (klik w drzewie, edycja, ShowNote)
    NoteItem? SelectedSearchResult { get; }     // zaznaczony wynik w zakladce pluginu search (ISearchTab); null gdy plugin wylaczony
    IReadOnlyList<string> Folders { get; }
    void ShowTab(string tabId);
    void ShowNote(NoteItem note);               // przelacza na widok notatki
    void Search(string query);                  // otwiera zakladke search z gotowym zapytaniem; no-op (ostrzezenie w logu) gdy plugin wylaczony
    Func<Note, bool>? TreeFilter { get; set; }  // zawezenie drzewa; null = bez filtra
    Task RefreshTreeAsync();                    // tylko dla TreeFilter; mutacje ida zdarzeniami
    TopLevel TopLevel { get; }                  // schowek, StorageProvider
}

// Zakladka pluginu search widziana przez host (IShell.Search / SelectedSearchResult): host szuka
// kontrybucji o Id "search" i rzutuje na ten interfejs, bez referencji do samego pluginu.
public interface ISearchTab
{
    void Search(string query);
    NoteItem? SelectedResult { get; }
}

// Stan edytora nowej notatki.
public interface IEditorContext
{
    string? Folder { get; }
    string Text { get; set; }
    string Status { set; }
    bool IsBusy { get; set; }
    Task OpenQuickNoteAsync(string folder);
}
