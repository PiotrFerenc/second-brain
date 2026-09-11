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
    private const string DailyFolder = "Dziennik";

    public const int TabEditor = 0;
    public const int TabSearch = 1;
    public const int TabNote = 2;
    public const int TabDaily = 3;
    public const int TabTrash = 4;

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

    [RelayCommand]
    private void ApplyTemplate(string templateName)
    {
        NoteText = templateName switch
        {
            "spotkanie" => "## Spotkanie\nData: \nUczestnicy: \n\n### Ustalenia\n- \n\n### Kolejne kroki\n- \n",
            "pomysl" => "## Pomysł\n\nProblem: \n\nRozwiązanie: \n\nDlaczego to działa: \n",
            "zadanie" => "## Zadanie\n\nCel: \n\nKroki:\n1. \n\nTermin: \n",
            _ => NoteText
        };
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

    // ---- Dziennik (notatka dnia) ----

    [ObservableProperty]
    public partial string DailyText { get; set; } = "";

    [ObservableProperty]
    public partial string DailyContent { get; set; } = "";

    [ObservableProperty]
    public partial string DailyStatus { get; set; } = "";

    [RelayCommand]
    private async Task LoadDailyAsync()
    {
        var title = DateTimeOffset.Now.ToString("yyyy-MM-dd");
        var notes = await noteStore.ListAsync(DailyFolder);
        DailyContent = notes.FirstOrDefault(n => n.Title == title)?.RawContent ?? "(brak wpisow dzisiaj)";
    }

    [RelayCommand]
    private async Task AppendDailyAsync()
    {
        if (string.IsNullOrWhiteSpace(DailyText))
            return;

        IsBusy = true;
        try
        {
            await vectorIndex.CreateFolderAsync(DailyFolder);

            var title = DateTimeOffset.Now.ToString("yyyy-MM-dd");
            var notes = await noteStore.ListAsync(DailyFolder);
            var existing = notes.FirstOrDefault(n => n.Title == title);
            var now = DateTimeOffset.UtcNow;
            var timestamp = DateTimeOffset.Now.ToString("HH:mm");

            var note = existing is null
                ? new Note(Guid.NewGuid(), title, $"[{timestamp}] {DailyText}", "", [], now, now)
                : existing with { RawContent = $"{existing.RawContent}\n[{timestamp}] {DailyText}", UpdatedAt = now };

            var result = await compressor.CompressAsync(note.RawContent);
            note = note with { CompressedContent = result.CompressedContent, Tags = result.Tags };

            var path = await noteStore.SaveAsync(DailyFolder, note);
            note = note with { FilePath = path };

            var vector = await embedder.EmbedAsync(note.CompressedContent);
            await vectorIndex.UpsertAsync(DailyFolder, note, vector);

            DailyContent = note.RawContent;
            DailyText = "";
            DailyStatus = "Dodano.";

            await LoadTreeAsync();
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

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    [RelayCommand]
    private void ShowEditorTab() => SelectedTabIndex = TabEditor;

    [RelayCommand]
    private void ShowSearchTab() => SelectedTabIndex = TabSearch;

    [RelayCommand]
    private void ShowDailyTab() => SelectedTabIndex = TabDaily;

    // ---- Start ----

    [RelayCommand]
    private async Task InitializeAsync()
    {
        await LoadTreeAsync();
        await LoadTrashAsync();
        await LoadDailyAsync();
    }
}
