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
    INoteStore noteStore) : ViewModelBase
{
    public ObservableCollection<string> Folders { get; } = [];

    [ObservableProperty]
    public partial string? SelectedFolder { get; set; }

    [ObservableProperty]
    public partial string NewFolderName { get; set; } = "";

    [ObservableProperty]
    public partial string NoteText { get; set; } = "";

    [ObservableProperty]
    public partial string EditorStatus { get; set; } = "";

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = "";

    [ObservableProperty]
    public partial SearchResultItem? SelectedResult { get; set; }

    [ObservableProperty]
    public partial SearchResultItem? SelectedFolderNote { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool HasSearched { get; set; }

    [ObservableProperty]
    public partial bool HasResults { get; set; }

    [ObservableProperty]
    public partial bool HasFolders { get; set; }

    [ObservableProperty]
    public partial bool HasFolderNotes { get; set; }

    public ObservableCollection<SearchResultItem> SearchResults { get; } = [];
    public ObservableCollection<SearchResultItem> FolderNotes { get; } = [];

    partial void OnSelectedFolderChanged(string? value)
    {
        NoteText = "";
        EditorStatus = "";
        SearchQuery = "";
        SearchResults.Clear();
        SelectedResult = null;
        HasSearched = false;
        HasResults = false;

        _ = LoadFolderNotesCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task LoadFoldersAsync()
    {
        Folders.Clear();
        foreach (var folder in await vectorIndex.ListFoldersAsync())
            Folders.Add(folder);

        SelectedFolder ??= Folders.FirstOrDefault();
        HasFolders = Folders.Count > 0;
    }

    [RelayCommand]
    private async Task CreateFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(NewFolderName))
            return;

        var created = await vectorIndex.CreateFolderAsync(NewFolderName);
        if (created)
            Folders.Add(NewFolderName);

        SelectedFolder = NewFolderName;
        NewFolderName = "";
        HasFolders = Folders.Count > 0;
    }

    [RelayCommand]
    private async Task LoadFolderNotesAsync()
    {
        FolderNotes.Clear();
        SelectedFolderNote = null;

        if (SelectedFolder is null)
        {
            HasFolderNotes = false;
            return;
        }

        foreach (var note in await noteStore.ListAsync(SelectedFolder))
            FolderNotes.Add(new SearchResultItem(note.Title, string.Join(", ", note.Tags), 0f, note.RawContent));

        SelectedFolderNote = FolderNotes.FirstOrDefault();
        HasFolderNotes = FolderNotes.Count > 0;
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

            var note = new Note(Guid.NewGuid(), result.Title, NoteText, result.CompressedContent, [], now, now);

            EditorStatus = "Zapisuje plik...";
            var path = await noteStore.SaveAsync(SelectedFolder, note);
            note = note with { FilePath = path };

            EditorStatus = "Licze embedding...";
            var vector = await embedder.EmbedAsync(note.CompressedContent);
            await vectorIndex.UpsertAsync(SelectedFolder, note, vector);

            EditorStatus = $"Zapisano: {result.Title}";
            NoteText = "";

            await LoadFolderNotesAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        SearchResults.Clear();
        SelectedResult = null;
        HasSearched = true;

        if (SelectedFolder is null || string.IsNullOrWhiteSpace(SearchQuery))
            return;

        IsBusy = true;
        try
        {
            var queryVector = await embedder.EmbedAsync(SearchQuery);
            var candidates = await vectorIndex.SearchAsync(SelectedFolder, queryVector, limit: 20);
            var reranked = await reranker.RerankAsync(SearchQuery, candidates);

            foreach (var r in reranked.Take(10))
            {
                var note = r.Note;
                if (!string.IsNullOrEmpty(r.Note.FilePath) && File.Exists(r.Note.FilePath))
                    note = await noteStore.LoadAsync(r.Note.FilePath);

                SearchResults.Add(new SearchResultItem(note.Title, string.Join(", ", note.Tags), r.Score, note.RawContent));
            }

            SelectedResult = SearchResults.FirstOrDefault();
            HasResults = SearchResults.Count > 0;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
