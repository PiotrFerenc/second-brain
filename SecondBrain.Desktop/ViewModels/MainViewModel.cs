using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecondBrain.Core;

namespace SecondBrain.Desktop.ViewModels;

public partial class MainViewModel(
    IVectorIndex vectorIndex,
    IEmbedder embedder,
    IReranker reranker,
    ICompressor compressor,
    IAnswerSynthesizer answerSynthesizer,
    INoteStore noteStore) : ViewModelBase
{
    public const int TabEditor = 0;
    public const int TabSearch = 1;
    public const int TabNote = 2;
    public const int TabTrash = 3;

    // ---- Drzewo (foldery + notatki, w tym zagniezdzone podstrony) ----

    public ObservableCollection<TreeItem> Tree { get; } = [];

    [ObservableProperty]
    public partial TreeItem? SelectedTreeItem { get; set; }

    [ObservableProperty]
    public partial string? SelectedFolder { get; set; }

    [ObservableProperty]
    public partial SearchResultItem? SelectedNote { get; set; }

    [ObservableProperty]
    public partial string BacklinksText { get; set; } = "";

    [ObservableProperty]
    public partial bool HasFolders { get; set; }

    partial void OnSelectedTreeItemChanged(TreeItem? value)
    {
        if (value is null)
            return;

        SelectedFolder = value.OwningFolder;

        if (!value.IsFolder && value.Note is not null)
        {
            SelectedNote = value.Note;
            SelectedTabIndex = TabNote;
            _ = LoadBacklinksAsync();
        }
    }

    [RelayCommand]
    private async Task LoadTreeAsync()
    {
        var selectedNoteId = SelectedNote?.Id;
        Tree.Clear();

        foreach (var folder in await vectorIndex.ListFoldersAsync())
        {
            var notes = await noteStore.ListAsync(folder);
            var folderNode = new TreeItem { DisplayName = folder, IsFolder = true, OwningFolder = folder };
            BuildNoteTree(folderNode.Children, notes, null, folder);
            Tree.Add(folderNode);
        }

        HasFolders = Tree.Count > 0;

        if (selectedNoteId is { } id)
            SelectedNote = FindNote(Tree, id);
    }

    private static void BuildNoteTree(ObservableCollection<TreeItem> target, IReadOnlyList<Note> notes, Guid? parentId, string owningFolder)
    {
        foreach (var note in notes.Where(n => n.ParentId == parentId))
        {
            var item = new TreeItem
            {
                DisplayName = note.Title,
                IsFolder = false,
                OwningFolder = owningFolder,
                Note = ToItem(note)
            };
            BuildNoteTree(item.Children, notes, note.Id, owningFolder);
            target.Add(item);
        }
    }

    private static SearchResultItem? FindNote(IEnumerable<TreeItem> nodes, Guid id)
    {
        foreach (var node in nodes)
        {
            if (node.Note?.Id == id)
                return node.Note;

            var found = FindNote(node.Children, id);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static SearchResultItem ToItem(Note note) =>
        new(note.Id, note.Title, string.Join(", ", note.Tags), 0f, note.RawContent, note.FilePath, note.ParentId, note.Pinned);

    // ---- Foldery ----

    [ObservableProperty]
    public partial string NewFolderName { get; set; } = "";

    [ObservableProperty]
    public partial bool ConfirmDeleteFolder { get; set; }

    [RelayCommand]
    private async Task CreateFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(NewFolderName))
            return;

        await vectorIndex.CreateFolderAsync(NewFolderName);
        SelectedFolder = NewFolderName;
        NewFolderName = "";

        await LoadTreeAsync();
    }

    // ponytail: usuniecie folderu kasuje kolekcje w Qdrant i cale notatki na dysku
    // (trwale, bez kosza - pelne cofniecie wymagaloby klonowania kolekcji Qdrant, co
    // jest niewspolmiernie drogie do tego jak rzadko to sie zdarza). Dwa kliknieca jako
    // jedyna ochrona przed pomylka.
    [RelayCommand]
    private async Task DeleteFolderAsync()
    {
        if (SelectedFolder is null)
            return;

        if (!ConfirmDeleteFolder)
        {
            ConfirmDeleteFolder = true;
            return;
        }

        var folder = SelectedFolder;
        await vectorIndex.DeleteFolderAsync(folder);
        await noteStore.DeleteFolderAsync(folder);

        SelectedFolder = null;
        SelectedNote = null;
        ConfirmDeleteFolder = false;

        await LoadTreeAsync();
    }

    // ---- Edytor / nowa notatka ----

    [ObservableProperty]
    public partial string NoteText { get; set; } = "";

    [ObservableProperty]
    public partial string NoteTagsInput { get; set; } = "";

    [ObservableProperty]
    public partial string EditorStatus { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public ObservableCollection<SearchResultItem> ParentOptions { get; } = [];

    [ObservableProperty]
    public partial SearchResultItem? SelectedParentOption { get; set; }

    partial void OnSelectedFolderChanged(string? value)
    {
        NoteText = "";
        NoteTagsInput = "";
        EditorStatus = "";
        SearchQuery = "";
        SynthesizedAnswer = "";
        HasAnswer = false;
        SearchResults.Clear();
        SelectedResult = null;
        HasSearched = false;
        HasResults = false;
        ConfirmDeleteFolder = false;
        SelectedParentOption = null;

        _ = LoadParentOptionsAsync();
    }

    [RelayCommand]
    private async Task LoadParentOptionsAsync()
    {
        ParentOptions.Clear();
        if (SelectedFolder is null)
            return;

        foreach (var note in await noteStore.ListAsync(SelectedFolder))
            ParentOptions.Add(ToItem(note));
    }

    public ObservableCollection<NoteTemplate> Templates { get; } = [];

    [RelayCommand]
    private async Task LoadTemplatesAsync()
    {
        Templates.Clear();
        foreach (var t in await noteStore.ListTemplatesAsync())
            Templates.Add(t);
    }

    [RelayCommand]
    private void ApplyTemplate(NoteTemplate? template)
    {
        if (template is not null)
            NoteText = template.Content;
    }

    [RelayCommand]
    private async Task SaveNoteAsync()
    {
        if (SelectedFolder is null || string.IsNullOrWhiteSpace(NoteText))
        {
            EditorStatus = "Wybierz folder i wpisz tresc notatki.";
            return;
        }

        IsBusy = true;
        try
        {
            EditorStatus = "Kompresuje...";
            var now = DateTimeOffset.UtcNow;
            var result = await compressor.CompressAsync(NoteText);

            var tags = string.IsNullOrWhiteSpace(NoteTagsInput)
                ? result.Tags
                : NoteTagsInput.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var note = new Note(Guid.NewGuid(), result.Title, NoteText, result.CompressedContent, tags,
                now, now, ParentId: SelectedParentOption?.Id);

            EditorStatus = "Zapisuje plik...";
            var path = await noteStore.SaveAsync(SelectedFolder, note);
            note = note with { FilePath = path };

            EditorStatus = "Licze embedding...";
            var vector = await embedder.EmbedAsync(note.CompressedContent);
            await vectorIndex.UpsertAsync(SelectedFolder, note, vector);

            // "Auto-linkowanie": zamiast prosic LLM o zgadywanie tytulow (ryzyko halucynacji),
            // uzywamy juz policzonego wektora notatki do wyszukania faktycznie podobnych.
            var related = await vectorIndex.SearchAsync(SelectedFolder, vector, limit: 4);
            var relatedTitles = related.Where(r => r.Note.Id != note.Id).Take(3).Select(r => r.Note.Title).ToList();

            EditorStatus = relatedTitles.Count > 0
                ? $"Zapisano: {result.Title}\nMoże powiązane: {string.Join(", ", relatedTitles)}"
                : $"Zapisano: {result.Title}";

            NoteText = "";
            NoteTagsInput = "";
            SelectedParentOption = null;

            await LoadTreeAsync();
            await LoadParentOptionsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- Notatka (podglad wybranej w drzewie) ----

    [RelayCommand]
    private async Task TogglePinAsync()
    {
        if (SelectedNote is null || SelectedFolder is null)
            return;

        var note = await noteStore.LoadAsync(SelectedNote.FilePath);
        note = note with { Pinned = !note.Pinned, UpdatedAt = DateTimeOffset.UtcNow };
        await noteStore.SaveAsync(SelectedFolder, note);

        SelectedNote = ToItem(note);
        await LoadTreeAsync();
    }

    private async Task LoadBacklinksAsync()
    {
        BacklinksText = "";
        if (SelectedNote is null || SelectedFolder is null)
            return;

        var notes = await noteStore.ListAsync(SelectedFolder);
        var referencing = notes
            .Where(n => n.Id != SelectedNote.Id &&
                        n.RawContent.Contains($"[[{SelectedNote.Title}]]", StringComparison.OrdinalIgnoreCase))
            .Select(n => n.Title)
            .ToList();

        BacklinksText = referencing.Count > 0 ? string.Join(", ", referencing) : "Brak.";
    }

    [RelayCommand]
    private async Task DeleteNoteAsync(SearchResultItem? item)
    {
        item ??= SelectedNote;
        if (item is null || SelectedFolder is null || string.IsNullOrEmpty(item.FilePath))
            return;

        await vectorIndex.DeleteNoteAsync(SelectedFolder, item.Id);
        await noteStore.MoveToTrashAsync(SelectedFolder, item.FilePath);

        if (SelectedResult == item)
            SelectedResult = null;
        if (SelectedNote == item)
            SelectedNote = null;

        SearchResults.Remove(item);
        HasResults = SearchResults.Count > 0;

        await LoadTreeAsync();
    }

    // ---- Wyszukiwanie ----

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = "";

    [ObservableProperty]
    public partial SearchResultItem? SelectedResult { get; set; }

    [ObservableProperty]
    public partial string SynthesizedAnswer { get; set; } = "";

    [ObservableProperty]
    public partial bool HasAnswer { get; set; }

    [ObservableProperty]
    public partial bool HasSearched { get; set; }

    [ObservableProperty]
    public partial bool HasResults { get; set; }

    public ObservableCollection<SearchResultItem> SearchResults { get; } = [];

    [RelayCommand]
    private async Task SearchAsync()
    {
        SearchResults.Clear();
        SelectedResult = null;
        HasSearched = true;
        SynthesizedAnswer = "";
        HasAnswer = false;

        if (SelectedFolder is null || string.IsNullOrWhiteSpace(SearchQuery))
            return;

        IsBusy = true;
        try
        {
            var queryVector = await embedder.EmbedAsync(SearchQuery);
            var candidates = await vectorIndex.SearchAsync(SelectedFolder, queryVector, limit: 20);
            var reranked = await reranker.RerankAsync(SearchQuery, candidates);

            var notesForAnswer = new List<Note>();

            foreach (var r in reranked.Take(10))
            {
                var note = r.Note;
                if (!string.IsNullOrEmpty(r.Note.FilePath) && File.Exists(r.Note.FilePath))
                    note = await noteStore.LoadAsync(r.Note.FilePath);

                SearchResults.Add(new SearchResultItem(note.Id, note.Title, string.Join(", ", note.Tags), r.Score, note.RawContent, note.FilePath, note.ParentId, note.Pinned));

                if (notesForAnswer.Count < 5)
                    notesForAnswer.Add(note);
            }

            SelectedResult = SearchResults.FirstOrDefault();
            HasResults = SearchResults.Count > 0;

            if (notesForAnswer.Count > 0)
            {
                SynthesizedAnswer = await answerSynthesizer.SynthesizeAsync(SearchQuery, notesForAnswer);
                HasAnswer = !string.IsNullOrWhiteSpace(SynthesizedAnswer);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- Kosz ----

    public ObservableCollection<TrashItem> TrashItems { get; } = [];

    [ObservableProperty]
    public partial TrashItem? SelectedTrashItem { get; set; }

    [ObservableProperty]
    public partial bool HasTrash { get; set; }

    [RelayCommand]
    private async Task LoadTrashAsync()
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

        var restored = await noteStore.RestoreFromTrashAsync(item.TrashPath);
        var vector = await embedder.EmbedAsync(restored.Note.CompressedContent);
        await vectorIndex.UpsertAsync(restored.OriginalFolder, restored.Note, vector);

        await LoadTrashAsync();
        await LoadTreeAsync();
    }

    [RelayCommand]
    private async Task PurgeFromTrashAsync(TrashItem? item)
    {
        item ??= SelectedTrashItem;
        if (item is null)
            return;

        await noteStore.PurgeTrashAsync(item.TrashPath);
        SelectedTrashItem = null;
        await LoadTrashAsync();
    }

    // ---- Zakladki / skroty klawiszowe ----
    // Szukaj i Kosz sa dostepne tylko z paska narzedzi (nie maja wlasnego naglowka
    // w prawym panelu) - stad wlasne flagi widoczności zamiast TabControl.SelectedIndex.

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; } = TabEditor;

    [ObservableProperty]
    public partial bool IsEditorTabActive { get; set; } = true;

    [ObservableProperty]
    public partial bool IsSearchTabActive { get; set; }

    [ObservableProperty]
    public partial bool IsNoteTabActive { get; set; }

    [ObservableProperty]
    public partial bool IsTrashTabActive { get; set; }

    partial void OnSelectedTabIndexChanged(int value)
    {
        IsEditorTabActive = value == TabEditor;
        IsSearchTabActive = value == TabSearch;
        IsNoteTabActive = value == TabNote;
        IsTrashTabActive = value == TabTrash;
    }

    [RelayCommand]
    private void ShowEditorTab() => SelectedTabIndex = TabEditor;

    [RelayCommand]
    private void ShowSearchTab() => SelectedTabIndex = TabSearch;

    [RelayCommand]
    private void ShowNoteTab() => SelectedTabIndex = TabNote;

    [RelayCommand]
    private void ShowTrashTab() => SelectedTabIndex = TabTrash;

    // ---- Start ----

    [RelayCommand]
    private async Task InitializeAsync()
    {
        await LoadTreeAsync();
        await LoadTrashAsync();
        await LoadTemplatesAsync();
    }
}
