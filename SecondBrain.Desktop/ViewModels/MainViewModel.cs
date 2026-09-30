using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecondBrain.Core;
using SecondBrain.Infrastructure;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Desktop.ViewModels;

public partial class MainViewModel(
    IVectorIndex vectorIndex,
    INoteStore noteStore,
    NotePipeline pipeline,
    IAgent agent,
    IAgentSessionStore agentSessionStore) : ViewModelBase
{
    public const int TabEditor = 0;
    public const int TabNote = 2;
    public const int TabAgent = 6;

    // ---- Drzewo (foldery + notatki, w tym zagniezdzone podstrony) ----

    public ObservableCollection<TreeItem> Tree { get; } = [];

    [ObservableProperty]
    public partial TreeItem? SelectedTreeItem { get; set; }

    [ObservableProperty]
    public partial string? SelectedFolder { get; set; }

    [ObservableProperty]
    public partial SearchResultItem? SelectedNote { get; set; }

    [ObservableProperty]
    public partial bool HasFolders { get; set; }

    // Aktywny filtr po tagu (klikniecie w chip w widoku Notatka) - zawezia drzewo do
    // notatek majacych ten tag, ze wszystkich folderow, dopoki nie zostanie wyczyszczony.
    [ObservableProperty]
    public partial string? ActiveTagFilter { get; set; }

    // Filtr drzewa ustawiany przez pluginy (IShell.TreeFilter); null = bez filtra.
    public Func<Note, bool>? TreeFilter { get; set; }

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
        }
    }

    partial void OnSelectedNoteChanged(SearchResultItem? value)
    {
        IsEditingNote = false;
        NoteEditStatus = "";
    }

    [RelayCommand]
    private async Task LoadTreeAsync()
    {
        var selectedNoteId = SelectedNote?.Id;
        var expandedKeys = new HashSet<string>();
        CollectExpandedKeys(Tree, expandedKeys);
        Tree.Clear();

        foreach (var folder in await vectorIndex.ListFoldersAsync())
        {
            var notes = await noteStore.ListAsync(folder);

            if (TreeFilter is not null)
                notes = notes.Where(TreeFilter).ToList();

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

        ApplyExpandedKeys(Tree, expandedKeys);
        HasFolders = Tree.Count > 0;
        RebuildMentionNames();

        if (selectedNoteId is { } id)
            SelectedNote = FindNote(Tree, id);
    }

    // LoadTreeAsync przebudowuje drzewo od zera (nowe instancje TreeItem) po kazdej akcji
    // (dodanie/usuniecie notatki, drag&drop, itd.), wiec bez tego uzytkownik traci
    // rozwiniecie galezi przy kazdym odswiezeniu - klucz po folderze/id notatki przenosi
    // stan rozwiniecia ze starych wezlow na nowe.
    private static string TreeKey(TreeItem item) => item.IsFolder ? $"F:{item.OwningFolder}" : $"N:{item.Note?.Id}";

    private static void CollectExpandedKeys(IEnumerable<TreeItem> nodes, HashSet<string> expanded)
    {
        foreach (var node in nodes)
        {
            if (node.IsExpanded)
                expanded.Add(TreeKey(node));
            CollectExpandedKeys(node.Children, expanded);
        }
    }

    private static void ApplyExpandedKeys(IEnumerable<TreeItem> nodes, HashSet<string> expanded)
    {
        foreach (var node in nodes)
        {
            if (expanded.Contains(TreeKey(node)))
                node.IsExpanded = true;
            ApplyExpandedKeys(node.Children, expanded);
        }
    }

    // Nazwy folderow i notatek do podpowiedzi @-wzmianek w czacie agenta - z juz
    // zaladowanego drzewa, bez dodatkowego zapytania do noteStore przy kazdym wpisanym znaku.
    private List<string> _mentionNames = [];

    private void RebuildMentionNames()
    {
        var names = new List<string>();
        foreach (var folder in Tree)
        {
            names.Add(folder.DisplayName);
            CollectNoteNames(folder.Children, names);
        }
        _mentionNames = names;
    }

    private static void CollectNoteNames(IEnumerable<TreeItem> nodes, List<string> names)
    {
        foreach (var node in nodes)
        {
            if (!node.IsFolder)
                names.Add(node.DisplayName);
            CollectNoteNames(node.Children, names);
        }
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
        new(note.Id, note.Title, note.Tags, 0f, note.RawContent, note.FilePath, note.ParentId, note.Pinned, folder, note.CreatedAt);

    // Drag&drop w drzewie: zagniezdzenie jednej notatki pod druga (ten sam folder - drzewo
    // buduje sie per-folder, wiec przenoszenie miedzy folderami zostaje agentowi/narzedziu).
    public async Task ReparentNoteAsync(TreeItem dragged, TreeItem target)
    {
        if (dragged.Note is null || target.Note is null || dragged == target)
            return;
        if (dragged.OwningFolder != target.OwningFolder)
            return;
        if (target.Note.Id == dragged.Note.Id || dragged.Note.ParentId == target.Note.Id)
            return;
        if (IsDescendant(dragged, target.Note.Id))
            return;

        var existing = await noteStore.LoadAsync(dragged.Note.FilePath);
        await pipeline.ReindexAsync(dragged.OwningFolder, existing with { ParentId = target.Note.Id, UpdatedAt = DateTimeOffset.UtcNow });

        await LoadTreeAsync();
    }

    private static bool IsDescendant(TreeItem node, Guid noteId)
    {
        foreach (var child in node.Children)
        {
            if (child.Note?.Id == noteId || IsDescendant(child, noteId))
                return true;
        }
        return false;
    }

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

        await pipeline.CreateFolderAsync(NewFolderName);
        SelectedFolder = NewFolderName;
        NewFolderName = "";

        await LoadTreeAsync();
    }

    // ponytail: usuniecie folderu kasuje plik indeksu wektorowego i cale notatki na dysku
    // (trwale, bez kosza - pelne cofniecie wymagaloby klonowania pliku indeksu wektorowego, co
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

        await pipeline.DeleteFolderAsync(SelectedFolder);

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

    // Prawy klik na drzewie: "Dodaj notatke" na folderze ustawia go jako docelowy, na
    // notatce dodatkowo zagniezdza nowa notatke pod nia (SelectedParentOption).
    [RelayCommand]
    private async Task AddNoteHereAsync(TreeItem? item)
    {
        if (item is null)
            return;

        SelectedFolder = item.OwningFolder;
        await LoadParentOptionsAsync();
        SelectedParentOption = item.IsFolder ? null : ParentOptions.FirstOrDefault(p => p.Id == item.Note?.Id);

        SelectedTabIndex = TabEditor;
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
            EditorStatus = "Zapisuje...";

            // Tagi: reczne z pola, a gdy puste - propozycja LLM dolozona o najczestsze tagi
            // podobnych notatek (auto-tagowanie z sasiadow, ktorych pipeline i tak wyszukal).
            var manualTags = NoteTagsInput.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var added = await pipeline.AddAsync(SelectedFolder, NoteText, SelectedParentOption?.Id,
                chooseTags: (result, related) => manualTags.Length > 0 ? manualTags : SuggestTags(result.Tags, related));

            var statusLines = new List<string> { $"Zapisano: {added.Note.Title}" };
            if (added.Related.Count > 0)
                statusLines.Add($"Może powiązane: {string.Join(", ", added.Related.Select(n => n.Title))}");
            statusLines.AddRange(added.Notices);

            EditorStatus = string.Join("\n", statusLines);

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

    // ponytail: tagi sasiadow liczone czestosciowo (bez wag/podobienstwa), max 2 dolozone -
    // podmienic na cos madrzejszego gdy prosta czestosc zacznie realnie zawadzac.
    private static string[] SuggestTags(string[] baseTags, IReadOnlyList<Note> neighbors)
    {
        var extra = neighbors
            .SelectMany(n => n.Tags)
            .Where(t => !baseTags.Contains(t, StringComparer.OrdinalIgnoreCase))
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(2);

        return [.. baseTags, .. extra];
    }

    // ---- Notatka (podglad wybranej w drzewie) ----

    [RelayCommand]
    private async Task TogglePinAsync()
    {
        if (SelectedNote is null)
            return;

        var note = await noteStore.LoadAsync(SelectedNote.FilePath);
        note = await pipeline.ReindexAsync(SelectedNote.Folder, note with { Pinned = !note.Pinned, UpdatedAt = DateTimeOffset.UtcNow });

        SelectedNote = ToItem(note, SelectedNote.Folder);
        await LoadTreeAsync();
    }

    [ObservableProperty]
    public partial bool IsEditingNote { get; set; }

    [ObservableProperty]
    public partial string EditNoteText { get; set; } = "";

    [ObservableProperty]
    public partial string NoteEditStatus { get; set; } = "";

    [RelayCommand]
    private void StartEditNote()
    {
        if (SelectedNote is null)
            return;

        EditNoteText = SelectedNote.RawContent;
        NoteEditStatus = "";
        IsEditingNote = true;
    }

    [RelayCommand]
    private void CancelEditNote()
    {
        IsEditingNote = false;
        NoteEditStatus = "";
    }

    // Edycja = ten sam pipeline co nowa notatka (rekompresja -> zapis -> embedding -> upsert),
    // ale nadpisuje istniejacy plik zamiast tworzyc nowy: SaveAsync wylicza sciezke z
    // Id+CreatedAt.Year, wiec zachowanie tych dwoch pol z `existing` (przez `with`) trafia
    // z powrotem w ten sam plik i ten sam wpis w indeksie wektorowym zamiast duplikowac notatke.
    [RelayCommand]
    private async Task SaveNoteEditAsync()
    {
        if (SelectedNote is null || string.IsNullOrWhiteSpace(EditNoteText))
            return;

        IsBusy = true;
        try
        {
            NoteEditStatus = "Zapisuje...";
            var existing = await noteStore.LoadAsync(SelectedNote.FilePath);
            var note = await pipeline.EditAsync(SelectedNote.Folder, existing, EditNoteText);

            SelectedNote = ToItem(note, SelectedNote.Folder);
            IsEditingNote = false;
            NoteEditStatus = "";

            await LoadTreeAsync();
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

    // ---- Agent (czat z dostepem do calego programu przez narzedzia) ----
    // Globalny, nie ograniczony do aktualnie wybranego folderu - agent sam decyduje ktorych
    // narzedzi/folderow uzyc. Kazda rozmowa to osobna sesja zapisywana na dysk (IAgentSessionStore)
    // po kazdej turze, wiec przetrwa restart aplikacji - patrz sekcja "Sesje" nizej.

    private string _agentConversationState = "";
    private Guid? _currentSessionId;

    // Ustawiane na czas programowej zmiany SelectedAgentSession (po zapisie/nowej sesji),
    // zeby OnSelectedAgentSessionChanged nie probowal wtedy przeladowac AgentMessages -
    // to przeladowanie ma sie dziac tylko gdy user rzeczywiscie klika inna sesje na liscie.
    private bool _suppressSessionLoad;

    public ObservableCollection<AgentChatItem> AgentMessages { get; } = [];

    [ObservableProperty]
    public partial string AgentInputText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsAgentBusy { get; set; }

    [ObservableProperty]
    public partial AgentPendingAction? AgentPending { get; set; }

    // Podpowiedzi @-wzmianek (folderow/notatek) pod polem wpisywania - wolane z code-behind
    // widoku, ktory sledzi pozycje kursora w TextBox (to szczegol widoku, nie ViewModelu).
    public ObservableCollection<string> MentionSuggestions { get; } = [];

    [ObservableProperty]
    public partial bool ShowMentionSuggestions { get; set; }

    [ObservableProperty]
    public partial int MentionSuggestionIndex { get; set; }

    public void UpdateMentionQuery(string? query)
    {
        MentionSuggestions.Clear();
        if (query is not null)
        {
            foreach (var name in _mentionNames
                         .Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase))
                         .Distinct()
                         .Take(8))
                MentionSuggestions.Add(name);
        }

        MentionSuggestionIndex = 0;
        ShowMentionSuggestions = MentionSuggestions.Count > 0;
    }

    public void CloseMentionSuggestions()
    {
        ShowMentionSuggestions = false;
        MentionSuggestions.Clear();
    }

    [RelayCommand]
    private async Task SendAgentMessageAsync()
    {
        var message = AgentInputText.Trim();
        if (string.IsNullOrWhiteSpace(message) || IsAgentBusy)
            return;

        AgentMessages.Add(new AgentChatItem("user", message));
        AgentInputText = "";

        IsAgentBusy = true;
        try
        {
            var step = await agent.SendAsync(_agentConversationState, message);
            ApplyAgentStep(step);
            await PersistCurrentSessionAsync();
        }
        finally
        {
            IsAgentBusy = false;
        }
    }

    [RelayCommand]
    private Task AcceptAgentActionAsync() => ConfirmAgentActionAsync(approved: true);

    [RelayCommand]
    private Task RejectAgentActionAsync() => ConfirmAgentActionAsync(approved: false);

    private async Task ConfirmAgentActionAsync(bool approved)
    {
        if (AgentPending is null)
            return;

        AgentPending = null;
        IsAgentBusy = true;
        try
        {
            var step = await agent.ConfirmAsync(_agentConversationState, approved);
            ApplyAgentStep(step);
            await PersistCurrentSessionAsync();

            // Agent dziala na noteStore/vectorIndex bezposrednio, mijajac te same komendy
            // ktore normalnie odswiezaja UI (SaveNoteAsync itd.) - po
            // zaakceptowanej akcji trzeba wiec dociagnac stan recznie.
            if (approved)
            {
                await LoadTreeAsync();
            }
        }
        finally
        {
            IsAgentBusy = false;
        }
    }

    // ---- Sesje (lista zapisanych rozmow, przelaczanie, nowa, usuwanie) ----

    public ObservableCollection<AgentSession> AgentSessions { get; } = [];

    [ObservableProperty]
    public partial AgentSession? SelectedAgentSession { get; set; }

    [RelayCommand]
    private async Task LoadAgentSessionsAsync()
    {
        AgentSessions.Clear();
        foreach (var session in await agentSessionStore.ListAsync())
            AgentSessions.Add(session);
    }

    partial void OnSelectedAgentSessionChanged(AgentSession? value)
    {
        if (_suppressSessionLoad || value is null)
            return;

        _currentSessionId = value.Id;
        _agentConversationState = value.ConversationState;
        AgentPending = null;

        AgentMessages.Clear();
        foreach (var m in value.Messages)
            AgentMessages.Add(new AgentChatItem(m.Role, m.Text));
    }

    [RelayCommand]
    private void NewAgentSession()
    {
        _currentSessionId = null;
        _agentConversationState = "";
        AgentPending = null;
        AgentMessages.Clear();

        _suppressSessionLoad = true;
        SelectedAgentSession = null;
        _suppressSessionLoad = false;
    }

    [RelayCommand]
    private async Task DeleteAgentSessionAsync(AgentSession? session)
    {
        session ??= SelectedAgentSession;
        if (session is null)
            return;

        await agentSessionStore.DeleteAsync(session.Id);
        AgentSessions.Remove(session);

        if (_currentSessionId == session.Id)
            NewAgentSession();
    }

    // Zapisuje biezaca rozmowe na dysk po kazdej turze (nowa wiadomosc usera lub potwierdzona
    // akcja). Pierwszy zapis nadaje Id i tytul (z pierwszej wiadomosci usera) - kolejne
    // nadpisuja ten sam plik (SaveAsync w FileAgentSessionStore adresuje po Id).
    private async Task PersistCurrentSessionAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var existing = _currentSessionId is { } id ? AgentSessions.FirstOrDefault(s => s.Id == id) : null;
        var sessionId = _currentSessionId ??= Guid.NewGuid();

        var firstUserMessage = AgentMessages.FirstOrDefault(m => m.IsUser)?.Text ?? "Rozmowa";
        var title = firstUserMessage.Length > 60 ? firstUserMessage[..60] + "..." : firstUserMessage;

        var session = new AgentSession(
            sessionId,
            title,
            _agentConversationState,
            AgentMessages.Select(m => new AgentSessionMessage(m.Role, m.Text)).ToList(),
            existing?.CreatedAt ?? now,
            now);

        await agentSessionStore.SaveAsync(session);

        if (existing is not null)
            AgentSessions[AgentSessions.IndexOf(existing)] = session;
        else
            AgentSessions.Insert(0, session);

        _suppressSessionLoad = true;
        SelectedAgentSession = session;
        _suppressSessionLoad = false;
    }

    private void ApplyAgentStep(AgentStepResult step)
    {
        _agentConversationState = step.ConversationState;
        AgentPending = step.PendingAction;

        if (step.PendingAction is { } pending)
            AgentMessages.Add(new AgentChatItem("assistant", $"Chce: {pending.Summary}"));
        else if (!string.IsNullOrWhiteSpace(step.ReplyText))
            AgentMessages.Add(new AgentChatItem("assistant", step.ReplyText));
    }

    // ---- Zakladki / skroty klawiszowe ----
    // Wlasne flagi widocznosci zamiast TabControl.SelectedIndex (panele rdzenia + widok pluginu).

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; } = TabEditor;

    [ObservableProperty]
    public partial bool IsEditorTabActive { get; set; } = true;

    [ObservableProperty]
    public partial bool IsNoteTabActive { get; set; }

    [ObservableProperty]
    public partial bool IsAgentTabActive { get; set; }

    // Naglowek "Notatka / +" w prawym panelu ma sens tylko dla tych dwoch widokow -
    // zakladki pluginow maja wlasna zawartosc od samej gory.
    [ObservableProperty]
    public partial bool IsContentHeaderVisible { get; set; } = true;

    // ---- Zakladki pluginow (ITabContribution) ----
    // SelectedTabIndex = TabPlugin chowa wszystkie panele rdzenia; widok pluginu tworzony raz
    // (CreateView) i cache'owany, DataContext = kontrybucja.

    public const int TabPlugin = -1;

    public ObservableCollection<ITabContribution> ToolbarTabs { get; } = [];
    public ObservableCollection<ITabContribution> HeaderTabs { get; } = [];

    private readonly Dictionary<ITabContribution, Control> _pluginViews = [];

    [ObservableProperty]
    public partial Control? ActiveTabView { get; set; }

    [ObservableProperty]
    public partial bool IsPluginTabActive { get; set; }

    [RelayCommand]
    private async Task ShowPluginTabAsync(ITabContribution? tab)
    {
        if (tab is null)
            return;

        if (!_pluginViews.TryGetValue(tab, out var view))
        {
            view = tab.CreateView();
            view.DataContext = tab;
            _pluginViews[tab] = view;
        }

        ActiveTabView = view;
        SelectedTabIndex = TabPlugin;
        await tab.OnActivatedAsync(CancellationToken.None);
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        IsPluginTabActive = value == TabPlugin;
        IsEditorTabActive = value == TabEditor;
        IsNoteTabActive = value == TabNote;
        IsAgentTabActive = value == TabAgent;
        IsContentHeaderVisible = value is TabEditor or TabNote;
    }

    [RelayCommand]
    private void ShowEditorTab() => SelectedTabIndex = TabEditor;

    [RelayCommand]
    private void ShowNoteTab() => SelectedTabIndex = TabNote;

    [RelayCommand]
    private void ShowAgentTab() => SelectedTabIndex = TabAgent;

    // ---- Start ----

    [RelayCommand]
    private async Task InitializeAsync()
    {
        await LoadTreeAsync();
        await LoadAgentSessionsAsync();
    }
}
