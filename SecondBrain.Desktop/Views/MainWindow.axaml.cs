using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SecondBrain.Desktop.ViewModels;

namespace SecondBrain.Desktop.Views;

public partial class MainWindow : Window
{
    // Stan okna (rozmiar) obok katalogu notatek (~/SecondBrain/), nie w appsettings.json -
    // to czysto lokalny stan UI, nie konfiguracja providerow/kluczy API.
    private static readonly string SavedWindowSizePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SecondBrain", "window.json");

    private record SavedWindowSize(double Width, double Height);

    // X i minimalizacja tylko chowaja okno do tray (App.axaml TrayIcon) zamiast konczyc
    // program - realne zamkniecie tylko przez CloseFromTray (menu tray "Zamknij").
    private bool _reallyClosing;

    public MainWindow()
    {
        InitializeComponent();
        LoadWindowSize();

        Opened += async (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.PropertyChanged += (_, e) => OnViewModelPropertyChanged(vm, e);
                await vm.InitializeCommand.ExecuteAsync(null);
                BuildTemplateButtons(vm);
                BuildTagChips(vm);
            }
        };

        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty && WindowState == WindowState.Minimized)
                Hide();
        };

        Closing += OnClosing;

        MentionPopup.PlacementTarget = AgentInputBox;
    }

    // ponytail: tylko Width/Height, bez pozycji/SavedWindowSize (maksymalizacja) - jedno pole
    // wiecej do walidowania na wielomonitorowych ukladach za niewielka korzysc; dopisac
    // gdy realnie zabraknie.
    private void LoadWindowSize()
    {
        try
        {
            if (!File.Exists(SavedWindowSizePath))
                return;

            var state = JsonSerializer.Deserialize<SavedWindowSize>(File.ReadAllText(SavedWindowSizePath));
            if (state is null)
                return;

            // Wartosci z poprzedniego uruchomienia (np. na innym/wiekszym monitorze) moga
            // byc bezsensowne - trzymamy w rozsadnych granicach zamiast ich odrzucac calkiem.
            if (state.Width is >= 400 and <= 10000)
                Width = state.Width;
            if (state.Height is >= 300 and <= 10000)
                Height = state.Height;
        }
        catch
        {
            // Uszkodzony/nieczytelny plik - zostaja domyslne wymiary z XAML.
        }
    }

    private void SaveWindowSize()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SavedWindowSizePath)!);
            File.WriteAllText(SavedWindowSizePath, JsonSerializer.Serialize(new SavedWindowSize(Width, Height)));
        }
        catch
        {
            // Zapis stanu okna to najlepszy wysilek - awaria nie moze zablokowac zamkniecia.
        }
    }

    // Wolane z App.axaml.cs (tray -> "Zamknij") - jedyna droga do prawdziwego wyjscia z programu.
    public void CloseFromTray()
    {
        _reallyClosing = true;
        Close();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        SaveWindowSize();

        if (_reallyClosing)
            return;

        e.Cancel = true;
        Hide();
    }

    private void OnViewModelPropertyChanged(MainViewModel vm, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedNote))
            BuildTagChips(vm);
    }

    // Szablony i tagi sa listami dynamicznymi - prosciej dopisac przyciski w code-behind
    // niz wiazac Command z zewnetrznym DataContext przez ItemsControl.
    private void BuildTemplateButtons(MainViewModel vm)
    {
        TemplatesPanel.Children.Clear();
        foreach (var template in vm.Templates)
        {
            var button = new Button { Content = template.Name, Classes = { "subtleAction" } };
            button.Click += (_, _) => vm.NoteText = template.Content;
            TemplatesPanel.Children.Add(button);
        }
    }

    private void BuildTagChips(MainViewModel vm)
    {
        NoteTagsPanel.Children.Clear();
        foreach (var tag in vm.SelectedNote?.TagList ?? [])
        {
            var button = new Button { Content = tag, Classes = { "subtleAction" } };
            button.Click += (_, _) => vm.FilterByTagCommand.Execute(tag);
            NoteTagsPanel.Children.Add(button);
        }
    }

    private async void ImportFile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Wybierz plik do importu (kazda linia = nowa notatka)",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Tekst") { Patterns = ["*.txt", "*.md"] }]
        });

        if (files.Count == 0)
            return;

        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
            lines.Add(line);

        await vm.ImportLinesCommand.ExecuteAsync(lines);
    }

    // OCR ze schowka: obraz -> base64 -> IOcrExtractor -> tekst do wglądu w polu notatki
    // (nie zapisujemy od razu - OCR bywa niedokladny, user ma szanse poprawic przed Zapisz).
    private async void PasteImage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var clipboard = GetTopLevel(this)?.Clipboard;
        var data = clipboard is null ? null : await clipboard.TryGetDataAsync();
        var bitmapItem = data?.Items.FirstOrDefault(i => i.Formats.Contains(DataFormat.Bitmap));
        if (bitmapItem is null)
        {
            vm.EditorStatus = "Brak obrazka w schowku.";
            return;
        }

        if (await bitmapItem.TryGetRawAsync(DataFormat.Bitmap) is not Bitmap bitmap)
        {
            vm.EditorStatus = "Nie udalo sie odczytac obrazka ze schowka.";
            return;
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());

        await vm.RunOcrCommand.ExecuteAsync(stream.ToArray());
    }

    // Wyszukiwanie po obrazie: ten sam schowek->OCR co przy edytorze, ale wynik leci do
    // SearchByImageCommand (OCR -> SearchQuery -> SearchAsync) zamiast do pola notatki.
    private async void PasteImageSearch_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var clipboard = GetTopLevel(this)?.Clipboard;
        var data = clipboard is null ? null : await clipboard.TryGetDataAsync();
        var bitmapItem = data?.Items.FirstOrDefault(i => i.Formats.Contains(DataFormat.Bitmap));
        if (bitmapItem is null || await bitmapItem.TryGetRawAsync(DataFormat.Bitmap) is not Bitmap bitmap)
            return;

        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());

        await vm.SearchByImageCommand.ExecuteAsync(stream.ToArray());
    }

    // Drag&drop w drzewie: przeciagniecie jednej notatki na druga zagniezdza ja pod nia
    // (ReparentNoteAsync w ViewModelu pilnuje tego samego folderu i braku cykli).
    // In-process format niesie referencje do TreeItem wprost, bez (de)serializacji.
    private static readonly DataFormat<TreeItem> TreeNoteFormat = DataFormat.CreateInProcessFormat<TreeItem>("SecondBrain.TreeNote");

    private TreeItem? _treeDragCandidate;
    private PointerPressedEventArgs? _treeDragPressArgs;
    private Point _treeDragStartPoint;

    private void TreeItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: TreeItem { IsFolder: false } item } &&
            e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
        {
            _treeDragCandidate = item;
            _treeDragPressArgs = e;
            _treeDragStartPoint = e.GetPosition(null);
        }
    }

    private async void TreeItem_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_treeDragCandidate is null || _treeDragPressArgs is null ||
            !e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            return;

        if (Point.Distance(e.GetPosition(null), _treeDragStartPoint) < 6)
            return;

        var dragged = _treeDragCandidate;
        var pressArgs = _treeDragPressArgs;
        _treeDragCandidate = null;
        _treeDragPressArgs = null;

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(TreeNoteFormat, dragged));
        await DragDrop.DoDragDropAsync(pressArgs, transfer, DragDropEffects.Move);
    }

    private void TreeItem_DragOver(object? sender, DragEventArgs e)
    {
        var canDrop = sender is Control { DataContext: TreeItem { IsFolder: false } } && e.DataTransfer.Formats.Contains(TreeNoteFormat);
        e.DragEffects = canDrop ? DragDropEffects.Move : DragDropEffects.None;
    }

    private async void TreeItem_Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm ||
            sender is not Control { DataContext: TreeItem { IsFolder: false } target } ||
            e.DataTransfer.Items.FirstOrDefault()?.TryGetRaw(TreeNoteFormat) is not TreeItem dragged)
            return;

        e.Handled = true;
        await vm.ReparentNoteAsync(dragged, target);
    }

    // @-wzmianki w czacie agenta: pozycja kursora w TextBox to stan widoku (Avalonia nie ma
    // tego w bindowalnej formie), wiec wykrywanie tokenu "@..." przy kursorze i obsluga
    // strzalek/Enter/Tab/Escape zyje tu, a ViewModel dostaje juz gotowe zapytanie/wybor.
    private void AgentInput_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not TextBox box)
            return;

        vm.UpdateMentionQuery(ExtractMentionQuery(box.Text ?? "", box.CaretIndex));
    }

    // "@" zaczyna wzmianke tylko na poczatku tekstu lub po bialym znaku (jak w Slacku/Discordzie),
    // zeby nie lapac np. adresow e-mail wpisanych w tresci wiadomosci.
    private static string? ExtractMentionQuery(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);

        var at = text.LastIndexOf('@', Math.Max(0, caret - 1));
        if (at < 0 || (at > 0 && !char.IsWhiteSpace(text[at - 1])))
            return null;

        var token = text[(at + 1)..caret];
        return token.Any(char.IsWhiteSpace) ? null : token;
    }

    private void AgentInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not TextBox box)
            return;

        if (vm.ShowMentionSuggestions)
        {
            switch (e.Key)
            {
                case Key.Down:
                    vm.MentionSuggestionIndex = Math.Min(vm.MentionSuggestionIndex + 1, vm.MentionSuggestions.Count - 1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    vm.MentionSuggestionIndex = Math.Max(vm.MentionSuggestionIndex - 1, 0);
                    e.Handled = true;
                    break;
                case Key.Enter:
                case Key.Tab:
                    AcceptMentionSuggestion(box, vm, vm.MentionSuggestions[vm.MentionSuggestionIndex]);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    vm.CloseMentionSuggestions();
                    e.Handled = true;
                    break;
            }
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            vm.SendAgentMessageCommand.Execute(null);
        }
    }

    private static void AcceptMentionSuggestion(TextBox box, MainViewModel vm, string name)
    {
        var text = box.Text ?? "";
        var caret = Math.Clamp(box.CaretIndex, 0, text.Length);
        var at = text.LastIndexOf('@', Math.Max(0, caret - 1));
        if (at < 0)
            return;

        box.Text = text[..(at + 1)] + name + " " + text[caret..];
        box.CaretIndex = at + 1 + name.Length + 1;
        vm.CloseMentionSuggestions();
    }

    private void MentionList_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || (e.Source as Control)?.DataContext is not string name)
            return;

        AcceptMentionSuggestion(AgentInputBox, vm, name);
        e.Handled = true;
        AgentInputBox.Focus();
    }

    // Schowek wymaga TopLevel, do ktorego ViewModel nie ma dostepu - stad w code-behind.
    private async void CopyAnswer_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            await CopyToClipboardAsync(vm.SynthesizedAnswer);
    }

    private async void CopySearchResult_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { SelectedResult: { } item })
            await CopyToClipboardAsync(item.RawContent);
    }

    private async void CopyNote_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { SelectedNote: { } item })
            await CopyToClipboardAsync(item.RawContent);
    }

    private async Task CopyToClipboardAsync(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var clipboard = GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
            return;

        using var transfer = new TextDataTransfer(text);
        await clipboard.SetDataAsync(transfer);
    }

    // SyncToAsyncDataTransfer (klasa Avalonii do tego samego) jest internal,
    // wiec wlasny minimalny wrapper tekstu pod IAsyncDataTransfer.
    private sealed class TextDataTransfer(string text) : IAsyncDataTransfer
    {
        public IReadOnlyList<DataFormat> Formats { get; } = [DataFormat.Text];
        public IReadOnlyList<IAsyncDataTransferItem> Items { get; } = [new TextDataTransferItem(text)];
        public void Dispose() { }
    }

    private sealed class TextDataTransferItem(string text) : IAsyncDataTransferItem
    {
        public IReadOnlyList<DataFormat> Formats { get; } = [DataFormat.Text];

        public Task<object?> TryGetRawAsync(DataFormat format) =>
            Task.FromResult(format == DataFormat.Text ? (object?)text : null);
    }
}
