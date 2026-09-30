using Avalonia.Controls;
using SecondBrain.Core;

namespace SecondBrain.Plugins.Sdk;

// Stan powloki i nawigacja - jedyne, co plugin wie o glownym oknie.
public interface IShell
{
    string? SelectedFolder { get; }
    NoteItem? SelectedNote { get; }
    NoteItem? SelectedSearchResult { get; }     // zaznaczony wynik w Szukaj (rdzen do partii B)
    IReadOnlyList<string> Folders { get; }
    void ShowTab(string tabId);
    void ShowNote(NoteItem note);               // przelacza na widok notatki
    void Search(string query);                  // Szukaj z gotowym zapytaniem (Szukaj jest jeszcze w rdzeniu - partia B przeniesie to do pluginu search)
    Func<Note, bool>? TreeFilter { get; set; }  // zawezenie drzewa; null = bez filtra
    Task RefreshTreeAsync();                    // tylko dla TreeFilter; mutacje ida zdarzeniami
    TopLevel TopLevel { get; }                  // schowek, StorageProvider
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
