using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecondBrain.Desktop.Views;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Desktop.Plugins;

// Menedzer wtyczek: lista z przelacznikami (zapis od razu do plugins.json), skutek po restarcie.
public sealed partial class PluginsTab(PluginManager manager) : ObservableObject, ITabContribution
{
    public string Id => "plugins";
    public string Title => "Wtyczki";
    public TabArea Placement => TabArea.SidebarToolbar;
    public int Order => int.MaxValue;
    public KeyGesture? Shortcut => null;

    public IReadOnlyList<PluginEntry> Plugins => manager.Plugins;

    public Control CreateView() => new PluginsView();

    // Best-effort: nowy proces z tego samego pliku wykonywalnego, potem prawdziwe zamkniecie
    // (X i minimalizacja tylko chowaja do tray).
    [RelayCommand]
    private void Restart()
    {
        try
        {
            if (Environment.ProcessPath is { } path)
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = false });
        }
        catch
        {
            // Nie udalo sie odpalic nowego procesu - i tak zamykamy, uzytkownik uruchomi recznie.
        }

        if ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is MainWindow window)
            window.CloseFromTray();
    }
}
