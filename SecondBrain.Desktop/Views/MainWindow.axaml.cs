using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SecondBrain.Desktop.ViewModels;

namespace SecondBrain.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
