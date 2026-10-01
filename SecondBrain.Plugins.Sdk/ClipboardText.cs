using Avalonia.Controls;
using Avalonia.Input;

namespace SecondBrain.Plugins.Sdk;

// Kopiowanie tekstu do schowka z dowolnego widoku (host i pluginy). SyncToAsyncDataTransfer
// (klasa Avalonii do tego samego) jest internal, wiec wlasny minimalny wrapper pod IAsyncDataTransfer.
public static class ClipboardText
{
    public static async Task SetAsync(TopLevel? topLevel, string? text)
    {
        if (string.IsNullOrEmpty(text) || topLevel?.Clipboard is not { } clipboard)
            return;

        using var transfer = new TextDataTransfer(text);
        await clipboard.SetDataAsync(transfer);
    }

    public static async Task<string?> GetAsync(TopLevel? topLevel)
    {
        var data = topLevel?.Clipboard is { } clipboard ? await clipboard.TryGetDataAsync() : null;
        var item = data?.Items.FirstOrDefault(i => i.Formats.Contains(DataFormat.Text));
        return item is null ? null : await item.TryGetRawAsync(DataFormat.Text) as string;
    }

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
