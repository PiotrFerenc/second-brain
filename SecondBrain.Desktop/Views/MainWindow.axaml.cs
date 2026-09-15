using System.ComponentModel;
using System.Linq;
using System.Text.Json;
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

        Closing += (_, _) => SaveWindowSize();
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
