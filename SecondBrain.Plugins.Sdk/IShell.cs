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

// Stan edytora nowej notatki. INotifyPropertyChanged, bo okno szybkiej notatki (plugin)
// binduje sie do niego wprost - host przekazuje zmiany z wlasnego VM.
public interface IEditorContext : System.ComponentModel.INotifyPropertyChanged
{
    string? Folder { get; }
    void SetFolder(string folder);              // wybor folderu docelowego (jak klik w drzewie)
    string Text { get; set; }
    string Status { get; set; }
    bool IsBusy { get; set; }
    Task SaveAsync();                           // ten sam zapis co przycisk "Zapisz" w edytorze
    Task OpenQuickNoteAsync(string folder);     // deleguje do IQuickNoteHost; no-op gdy plugin quicknote wylaczony
}

// Okno szybkiej notatki - dostarcza plugin quicknote; host tylko deleguje OpenQuickNoteAsync.
public interface IQuickNoteHost
{
    Task OpenAsync(string folder);
}
