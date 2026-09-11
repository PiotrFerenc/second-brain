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
    IConflictDetector conflictDetector,
    INoteStore noteStore) : ViewModelBase
{
    public const int TabEditor = 0;
    public const int TabSearch = 1;
    public const int TabNote = 2;
    public const int TabTrash = 3;
    public const int TabGaps = 4;
    public const int TabGlossary = 5;

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

    // Aktywny filtr po tagu (klikniecie w chip w widoku Notatka) - zawezia drzewo do
    // notatek majacych ten tag, ze wszystkich folderow, dopoki nie zostanie wyczyszczony.
    [ObservableProperty]
    public partial string? ActiveTagFilter { get; set; }

    [RelayCommand]
    private async Task ClearTagFilterAsync()
    {
        ActiveTagFilter = null;
        await LoadTreeAsync();
    }

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

            if (ActiveTagFilter is not null)
            {
                notes = notes.Where(n => n.Tags.Any(t => string.Equals(t, ActiveTagFilter, StringComparison.OrdinalIgnoreCase))).ToList();
                if (notes.Count == 0)
                    continue;
            }

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
                Note = ToItem(note, owningFolder)
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

    private static SearchResultItem ToItem(Note note, string folder) =>
        new(note.Id, note.Title, note.Tags, 0f, note.RawContent, note.FilePath, note.ParentId, note.Pinned, folder);

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
            ParentOptions.Add(ToItem(note, SelectedFolder));
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

            foreach (var def in result.Definitions ?? [])
                await noteStore.SaveGlossaryEntryAsync(def.Term, def.Definition, result.Title);

            EditorStatus = "Licze embedding...";
            var vector = await embedder.EmbedAsync(note.CompressedContent);
            await vectorIndex.UpsertAsync(SelectedFolder, note, vector);

            // "Auto-linkowanie": zamiast prosic LLM o zgadywanie tytulow (ryzyko halucynacji),
            // uzywamy juz policzonego wektora notatki do wyszukania faktycznie podobnych.
            var related = await vectorIndex.SearchAsync(SelectedFolder, vector, limit: 4);
            var relatedNotes = related.Where(r => r.Note.Id != note.Id).Take(3).Select(r => r.Note).ToList();
            var relatedTitles = relatedNotes.Select(n => n.Title).ToList();

            var statusLines = new List<string> { $"Zapisano: {result.Title}" };
            if (relatedTitles.Count > 0)
                statusLines.Add($"Może powiązane: {string.Join(", ", relatedTitles)}");

            if (relatedNotes.Count > 0)
            {
                var conflict = await conflictDetector.DetectAsync(note.CompressedContent, relatedNotes);
                if (conflict.HasConflict)
                    statusLines.Add($"Możliwa sprzeczność z \"{conflict.ConflictingTitle}\": {conflict.Explanation}");
            }

            EditorStatus = string.Join("\n", statusLines);

            NoteText = "";
            NoteTagsInput = "";
            SelectedParentOption = null;

            await LoadTreeAsync();
            await LoadParentOptionsAsync();
            await LoadGlossaryAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Import zbiorczy: kazda niepusta linia pliku przechodzi przez ten sam pipeline
    // co pojedyncza notatka (kompresja -> zapis -> embedding -> upsert). Bez wykrywacza
    // sprzecznosci - N linii to juz N wywolan LLM, kolejne podwoilyby koszt/czas importu.
    // Definicje do slownika zostaja, bo pochodza z tej samej kompresji (bez dodatkowego kosztu).
    [RelayCommand]
    private async Task ImportLinesAsync(IReadOnlyList<string> lines)
    {
        if (SelectedFolder is null)
        {
            EditorStatus = "Wybierz folder przed importem.";
            return;
        }

        var toImport = lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (toImport.Count == 0)
            return;

        IsBusy = true;
        try
        {
            var imported = 0;
            foreach (var line in toImport)
            {
                EditorStatus = $"Importuje {imported + 1}/{toImport.Count}...";
                var now = DateTimeOffset.UtcNow;
                var result = await compressor.CompressAsync(line);
                var note = new Note(Guid.NewGuid(), result.Title, line, result.CompressedContent, result.Tags, now, now);

                var path = await noteStore.SaveAsync(SelectedFolder, note);
                note = note with { FilePath = path };

                foreach (var def in result.Definitions ?? [])
                    await noteStore.SaveGlossaryEntryAsync(def.Term, def.Definition, result.Title);

                var vector = await embedder.EmbedAsync(note.CompressedContent);
                await vectorIndex.UpsertAsync(SelectedFolder, note, vector);
                imported++;
            }

            EditorStatus = $"Zaimportowano notatek: {imported}.";
            await LoadTreeAsync();
            await LoadParentOptionsAsync();
            await LoadGlossaryAsync();
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
        if (SelectedNote is null)
            return;

        var note = await noteStore.LoadAsync(SelectedNote.FilePath);
        note = note with { Pinned = !note.Pinned, UpdatedAt = DateTimeOffset.UtcNow };
        await noteStore.SaveAsync(SelectedNote.Folder, note);

        SelectedNote = ToItem(note, SelectedNote.Folder);
        await LoadTreeAsync();
    }

    private async Task LoadBacklinksAsync()
    {
        BacklinksText = "";
        if (SelectedNote is null)
            return;

        var notes = await noteStore.ListAsync(SelectedNote.Folder);
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
        if (item is null || string.IsNullOrEmpty(item.FilePath) || string.IsNullOrEmpty(item.Folder))
            return;

        await vectorIndex.DeleteNoteAsync(item.Folder, item.Id);
        await noteStore.MoveToTrashAsync(item.Folder, item.FilePath);

        if (SelectedResult == item)
            SelectedResult = null;
        if (SelectedNote == item)
            SelectedNote = null;

        SearchResults.Remove(item);
        HasResults = SearchResults.Count > 0;

        await LoadTreeAsync();
    }

    // ---- Wyszukiwanie (globalne, po wszystkich folderach) ----

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

    // Domyslnie globalnie (wszystkie foldery) - zaznaczenie zawezia do aktualnie
    // wybranego w drzewie folderu.
    [ObservableProperty]
    public partial bool SearchCurrentFolderOnly { get; set; }

    [RelayCommand]
    private async Task SearchAsync()
    {
        SearchResults.Clear();
        SelectedResult = null;
        HasSearched = true;
        SynthesizedAnswer = "";
        HasAnswer = false;

        if (string.IsNullOrWhiteSpace(SearchQuery))
            return;

        IsBusy = true;
        try
        {
            var queryVector = await embedder.EmbedAsync(SearchQuery);

            IReadOnlyList<string> foldersToSearch = SearchCurrentFolderOnly && SelectedFolder is not null
                ? [SelectedFolder]
                : await vectorIndex.ListFoldersAsync();

            var candidatesWithFolder = new List<(string Folder, ScoredNote Scored)>();
            foreach (var folder in foldersToSearch)
            {
                var candidates = await vectorIndex.SearchAsync(folder, queryVector, limit: 20);
                candidatesWithFolder.AddRange(candidates.Select(c => (folder, c)));
            }

            var folderById = candidatesWithFolder.ToDictionary(c => c.Scored.Note.Id, c => c.Folder);
            var reranked = await reranker.RerankAsync(SearchQuery, candidatesWithFolder.Select(c => c.Scored).ToList());

            var notesForAnswer = new List<Note>();

            foreach (var r in reranked.Take(10))
            {
                var note = r.Note;
                if (!string.IsNullOrEmpty(r.Note.FilePath) && File.Exists(r.Note.FilePath))
                    note = await noteStore.LoadAsync(r.Note.FilePath);

                var folder = folderById.GetValueOrDefault(r.Note.Id, "");
                SearchResults.Add(new SearchResultItem(note.Id, note.Title, note.Tags, r.Score, note.RawContent, note.FilePath, note.ParentId, note.Pinned, folder));

                if (notesForAnswer.Count < 5)
                    notesForAnswer.Add(note);
            }

            SelectedResult = SearchResults.FirstOrDefault();
            HasResults = SearchResults.Count > 0;

            if (notesForAnswer.Count > 0)
            {
                var answer = await answerSynthesizer.SynthesizeAsync(SearchQuery, notesForAnswer);
                SynthesizedAnswer = answer.Answer;
                HasAnswer = !string.IsNullOrWhiteSpace(SynthesizedAnswer);

                // "Luka w wiedzy": RAG jawnie mowi ze notatki nie zawieraja odpowiedzi -
                // zapisujemy pytanie, zeby nie zginelo, i user mial co dopisac.
                if (!answer.Answered)
                    await noteStore.LogGapAsync(SearchQuery);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Klikniecie w tag: zawezia DRZEWO folderow do notatek z tym tagiem (ze wszystkich
    // folderow), zamiast pokazywac plaska liste wynikow - patrz ActiveTagFilter/LoadTreeAsync.
    [RelayCommand]
    private async Task FilterByTagAsync(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return;

        ActiveTagFilter = tag;
        await LoadTreeAsync();
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

    // ---- Luki w wiedzy ----

    public ObservableCollection<GapItem> Gaps { get; } = [];

    [ObservableProperty]
    public partial GapItem? SelectedGap { get; set; }

    [ObservableProperty]
    public partial bool HasGaps { get; set; }

    [RelayCommand]
    private async Task LoadGapsAsync()
    {
        Gaps.Clear();
        foreach (var g in await noteStore.ListGapsAsync())
            Gaps.Add(new GapItem(g.Query, g.AskedAt, g.Path));

        HasGaps = Gaps.Count > 0;
    }

    [RelayCommand]
    private async Task ResolveGapAsync(GapItem? item)
    {
        item ??= SelectedGap;
        if (item is null)
            return;

        await noteStore.ResolveGapAsync(item.Path);
        SelectedGap = null;
        await LoadGapsAsync();
    }

    [RelayCommand]
    private async Task RetryGapSearchAsync(GapItem? item)
    {
        item ??= SelectedGap;
        if (item is null)
            return;

        SearchQuery = item.Query;
        SelectedTabIndex = TabSearch;
        await SearchAsync();
    }

    // ---- Auto-slownik ----

    public ObservableCollection<GlossaryEntry> GlossaryEntries { get; } = [];

    [ObservableProperty]
    public partial GlossaryEntry? SelectedGlossaryEntry { get; set; }

    [ObservableProperty]
    public partial bool HasGlossary { get; set; }

    [RelayCommand]
    private async Task LoadGlossaryAsync()
    {
        GlossaryEntries.Clear();
        foreach (var e in await noteStore.ListGlossaryAsync())
            GlossaryEntries.Add(e);

        HasGlossary = GlossaryEntries.Count > 0;
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

    [ObservableProperty]
    public partial bool IsGapsTabActive { get; set; }

    [ObservableProperty]
    public partial bool IsGlossaryTabActive { get; set; }

    // Naglowek "Notatka / +" w prawym panelu ma sens tylko dla tych dwoch widokow -
    // Szukaj, Kosz, Luki i Slownik maja wlasna zawartosc od samej gory.
    [ObservableProperty]
    public partial bool IsContentHeaderVisible { get; set; } = true;

    partial void OnSelectedTabIndexChanged(int value)
    {
        IsEditorTabActive = value == TabEditor;
        IsSearchTabActive = value == TabSearch;
        IsNoteTabActive = value == TabNote;
        IsTrashTabActive = value == TabTrash;
        IsGapsTabActive = value == TabGaps;
        IsGlossaryTabActive = value == TabGlossary;
        IsContentHeaderVisible = value is TabEditor or TabNote;
    }

    [RelayCommand]
    private void ShowEditorTab() => SelectedTabIndex = TabEditor;

    [RelayCommand]
    private void ShowSearchTab() => SelectedTabIndex = TabSearch;

    [RelayCommand]
    private void ShowNoteTab() => SelectedTabIndex = TabNote;

    [RelayCommand]
    private void ShowGlossaryTab() => SelectedTabIndex = TabGlossary;

    [RelayCommand]
    private void ShowTrashTab() => SelectedTabIndex = TabTrash;

    [RelayCommand]
    private void ShowGapsTab() => SelectedTabIndex = TabGaps;

    // ---- Start ----

    [RelayCommand]
    private async Task InitializeAsync()
    {
        await LoadTreeAsync();
        await LoadTrashAsync();
        await LoadTemplatesAsync();
        await LoadGapsAsync();
        await LoadGlossaryAsync();
    }
}
