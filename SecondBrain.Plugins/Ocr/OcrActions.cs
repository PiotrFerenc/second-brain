using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using SecondBrain.Core;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Ocr;

// Jedno miejsce odczytu obrazka ze schowka jako PNG (przed migracja skopiowane 3 razy w hoscie).
internal static class ClipboardImage
{
    public static async Task<byte[]?> ReadPngAsync(IShell shell)
    {
        var clipboard = shell.TopLevel.Clipboard;
        var data = clipboard is null ? null : await clipboard.TryGetDataAsync();
        var bitmapItem = data?.Items.FirstOrDefault(i => i.Formats.Contains(DataFormat.Bitmap));
        if (bitmapItem is null || await bitmapItem.TryGetRawAsync(DataFormat.Bitmap) is not Bitmap bitmap)
            return null;

        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }
}

// "Wklej obrazek ze schowka" w pasku edytora: tekst z OCR laduje w polu notatki do wgladu.
public sealed class OcrToEditorAction(IOcrExtractor ocr, IShell shell, IEditorContext editor) : ISlotContribution
{
    public string SlotId => "Editor.Toolbar";
    public int Order => 0;

    public Control CreateControl()
    {
        var button = new Button { Content = "Wklej obrazek ze schowka", Classes = { "subtleAction" } };
        button.Click += async (_, _) => await RunAsync();
        return button;
    }

    private async Task RunAsync()
    {
        var clipboard = shell.TopLevel.Clipboard;
        var data = clipboard is null ? null : await clipboard.TryGetDataAsync();
        if (data?.Items.Any(i => i.Formats.Contains(DataFormat.Bitmap)) != true)
        {
            editor.Status = "Brak obrazka w schowku.";
            return;
        }

        var png = await ClipboardImage.ReadPngAsync(shell);
        if (png is null)
        {
            editor.Status = "Nie udalo sie odczytac obrazka ze schowka.";
            return;
        }

        await OcrIntoEditorAsync(ocr, editor, png);
    }

    // Wspolne dla edytora i tray: te same statusy, ten sam brak auto-zapisu.
    internal static async Task OcrIntoEditorAsync(IOcrExtractor ocr, IEditorContext editor, byte[] png)
    {
        editor.IsBusy = true;
        try
        {
            editor.Status = "Odczytuje tekst z obrazka (OCR)...";
            editor.Text = await ocr.ExtractTextAsync(png, "image/png");
            editor.Status = "Tekst z obrazka wczytany - sprawdz i zapisz.";
        }
        finally
        {
            editor.IsBusy = false;
        }
    }
}

// "Szukaj po obrazku" obok pola zapytania: OCR jako zapytanie do Szukaj.
public sealed class OcrToSearchAction(IOcrExtractor ocr, IShell shell) : ISlotContribution
{
    public string SlotId => "Search.Toolbar";
    public int Order => 0;

    public Control CreateControl()
    {
        var button = new Button { Content = "Szukaj po obrazku", Classes = { "subtleAction" } };
        ToolTip.SetTip(button, "OCR obrazka ze schowka jako zapytanie");
        button.Click += async (_, _) =>
        {
            var png = await ClipboardImage.ReadPngAsync(shell);
            if (png is null)
                return;

            var text = await ocr.ExtractTextAsync(png, "image/png");
            if (!string.IsNullOrWhiteSpace(text))
                shell.Search(text);
        };
        return button;
    }
}

// Tray: "Nowa notatka > <folder> > Obrazek ze schowka (OCR)" - szybka notatka z OCR.
public sealed class OcrTrayItem(IOcrExtractor ocr, IShell shell, IEditorContext editor) : ITrayNewNoteContribution
{
    public NativeMenuItem Build(string folder)
    {
        var item = new NativeMenuItem("Obrazek ze schowka (OCR)");
        item.Click += async (_, _) =>
        {
            var png = await ClipboardImage.ReadPngAsync(shell);
            if (png is null)
                return;

            await editor.OpenQuickNoteAsync(folder);
            await OcrToEditorAction.OcrIntoEditorAsync(ocr, editor, png);
        };
        return item;
    }
}
