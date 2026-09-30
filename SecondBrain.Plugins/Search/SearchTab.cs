using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecondBrain.Core;
using SecondBrain.Infrastructure;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Search;

// Wyszukiwanie globalne (wszystkie foldery), opcjonalnie zawezone do folderu wybranego w drzewie.
public sealed partial class SearchTab(NoteSearch noteSearch, IAnswerSynthesizer answerSynthesizer, IEventBus events, IShell shell)
    : ObservableObject, ITabContribution, ISearchTab
{
    public string Id => "search";
    public string Title => "Szukaj";
    public TabArea Placement => TabArea.SidebarToolbar;
    public int Order => 10;
    public KeyGesture? Shortcut { get; } = new(Key.F, KeyModifiers.Control);

    public Control CreateView() => new SearchView();

    public Task OnActivatedAsync(CancellationToken ct)
    {
        OnPropertyChanged(nameof(SelectedFolder));
        return Task.CompletedTask;
    }

    // ponytail: IShell nie powiadamia o zmianie folderu - etykieta "tylko w folderze: X" odswieza
    // sie przy aktywacji zakladki i przy szukaniu; sam zakres szukania czyta shell w momencie szukania.
    public string? SelectedFolder => shell.SelectedFolder;

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = "";

    [ObservableProperty]
    public partial NoteItem? SelectedResult { get; set; }

    [ObservableProperty]
    public partial string SynthesizedAnswer { get; set; } = "";

    [ObservableProperty]
    public partial bool HasAnswer { get; set; }

    [ObservableProperty]
    public partial bool HasSearched { get; set; }

    [ObservableProperty]
    public partial bool HasResults { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public ObservableCollection<NoteItem> SearchResults { get; } = [];

    // Domyslnie globalnie (wszystkie foldery) - zaznaczenie zawezia do aktualnie
    // wybranego w drzewie folderu.
    [ObservableProperty]
    public partial bool SearchCurrentFolderOnly { get; set; }

    // ISearchTab: host (IShell.Search) ustawia zapytanie i odpala szukanie, np. "Szukaj ponownie" z luk.
    public void Search(string query)
    {
        SearchQuery = query;
        _ = SearchCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        SearchResults.Clear();
        SelectedResult = null;
        HasSearched = true;
        SynthesizedAnswer = "";
        HasAnswer = false;
        OnPropertyChanged(nameof(SelectedFolder));

        if (string.IsNullOrWhiteSpace(SearchQuery))
            return;

        IsBusy = true;
        try
        {
            IReadOnlyList<string>? foldersToSearch = SearchCurrentFolderOnly && SelectedFolder is not null ? [SelectedFolder] : null;
            var hits = await noteSearch.SearchAsync(SearchQuery, foldersToSearch, vectorLimit: 20);

            var notesForAnswer = new List<Note>();

            foreach (var (folder, note, score) in hits.Take(10))
            {
                SearchResults.Add(new NoteItem(note.Id, note.Title, note.Tags, score, note.RawContent, note.FilePath, note.ParentId, note.Pinned, folder, note.CreatedAt));

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
                // handler GapLogOnSearch (plugin gaps) zapisuje pytanie, zeby nie zginelo i user mial co dopisac.
                await events.PublishAsync(new SearchCompleted(SearchQuery, answer.Answered));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task CopyAnswerAsync() => ClipboardText.SetAsync(shell.TopLevel, SynthesizedAnswer);

    [RelayCommand]
    private Task CopyResultAsync() => ClipboardText.SetAsync(shell.TopLevel, SelectedResult?.RawContent);
}
