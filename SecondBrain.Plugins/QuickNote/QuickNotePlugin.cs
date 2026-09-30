using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.QuickNote;

// Szybka notatka (jak OneNote Win+N): male okno z tray "Nowa notatka > <folder> > Wpisz... / Ze schowka",
// zapis przez ten sam edytor co glowne okno (IEditorContext.SaveAsync).
public sealed class QuickNotePlugin : IPlugin
{
    public string Id => "quicknote";
    public string Name => "Szybka notatka";
    public string Description => "Male okno notatki z menu tray: wpisz tekst albo wklej ze schowka, bez otwierania glownego okna.";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<IQuickNoteHost, QuickNoteHost>();
        services.AddSingleton<ITrayNewNoteContribution, TypeTrayItem>();
        services.AddSingleton<ITrayNewNoteContribution, ClipboardTrayItem>();
    }
}

// Jedno okno naraz - kolejne otwarcie tylko przywraca i aktywuje istniejace.
public sealed class QuickNoteHost(IEditorContext editor) : IQuickNoteHost
{
    private QuickNoteWindow? _window;

    public Task OpenAsync(string folder)
    {
        editor.SetFolder(folder);

        if (_window is null)
        {
            _window = new QuickNoteWindow { DataContext = editor };
            _window.Closed += (_, _) => _window = null;
            _window.Show();
        }

        _window.WindowState = WindowState.Normal;
        _window.Activate();
        return Task.CompletedTask;
    }
}

public sealed class TypeTrayItem(IQuickNoteHost host) : ITrayNewNoteContribution
{
    public NativeMenuItem Build(string folder)
    {
        var item = new NativeMenuItem("Wpisz...");
        item.Click += async (_, _) => await host.OpenAsync(folder);
        return item;
    }
}

// Tekst ze schowka (nie obrazek) trafia od razu do pola notatki -
// user wciaz musi kliknac Zapisz, zeby dac szanse na poprawki przed kompresja/zapisem.
public sealed class ClipboardTrayItem(IQuickNoteHost host, IShell shell, IEditorContext editor) : ITrayNewNoteContribution
{
    public NativeMenuItem Build(string folder)
    {
        var item = new NativeMenuItem("Ze schowka");
        item.Click += async (_, _) =>
        {
            var clipboard = shell.TopLevel.Clipboard;
            var data = clipboard is null ? null : await clipboard.TryGetDataAsync();
            var textItem = data?.Items.FirstOrDefault(i => i.Formats.Contains(DataFormat.Text));
            if (textItem is null || await textItem.TryGetRawAsync(DataFormat.Text) is not string text || string.IsNullOrWhiteSpace(text))
                return;

            await host.OpenAsync(folder);
            editor.Text = text;
        };
        return item;
    }
}
