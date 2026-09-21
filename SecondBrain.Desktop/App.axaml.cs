using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Desktop.ViewModels;
using SecondBrain.Desktop.Views;

namespace SecondBrain.Desktop;

public partial class App : Application
{
    public static IServiceProvider Services { get; set; } = null!;

    private MainWindow? _mainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainViewModel>(),
            };
            desktop.MainWindow = _mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Tray ikona (kolo zegarka na Windows) z App.axaml - klik/"Pokaz" przywraca okno,
    // "Zamknij" to jedyna droga do prawdziwego wyjscia (X i minimalizacja tylko chowaja
    // do tray, patrz MainWindow.OnClosing / WindowState handler).
    private void TrayIcon_Clicked(object? sender, EventArgs e) => ShowMainWindow();

    private void ShowMenuItem_Click(object? sender, EventArgs e) => ShowMainWindow();

    private void ExitMenuItem_Click(object? sender, EventArgs e) => _mainWindow?.CloseFromTray();

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
            return;

        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Show();
        _mainWindow.Activate();
    }
}
