using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
                await vm.LoadFoldersCommand.ExecuteAsync(null);
        };
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

    private async void CopyFolderNote_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { SelectedFolderNote: { } item })
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
