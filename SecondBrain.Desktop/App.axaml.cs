using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Desktop.ViewModels;
using SecondBrain.Desktop.Views;

namespace SecondBrain.Desktop;

public partial class App : Application
{
    public static IServiceProvider Services { get; set; } = null!;

    private MainWindow? _mainWindow;
    private NativeMenu? _newNoteFolderMenu;

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
            SetupTrayIcon();
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Tray ikona (kolo zegarka na Windows), budowana w kodzie bo podmenu "Nowa notatka" jest
    // dynamiczne (lista folderow) - x:Name nie dziala na obiektach pod Application w XAML.
    // Klik na ikonie/"Pokaz" przywraca okno, "Zamknij" to jedyna droga do prawdziwego wyjscia
    // (X i minimalizacja tylko chowaja do tray, patrz MainWindow.OnClosing / WindowState handler).
    private void SetupTrayIcon()
    {
        _newNoteFolderMenu = new NativeMenu();
        _newNoteFolderMenu.Opening += (_, _) => RefreshNewNoteFolderMenu();

        var newNoteItem = new NativeMenuItem("Nowa notatka") { Menu = _newNoteFolderMenu };
        var showItem = new NativeMenuItem("Pokaż");
        showItem.Click += (_, _) => ShowMainWindow();
        var exitItem = new NativeMenuItem("Zamknij");
        exitItem.Click += (_, _) => _mainWindow?.CloseFromTray();

        var menu = new NativeMenu { newNoteItem, new NativeMenuItemSeparator(), showItem, exitItem };

        var trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SecondBrain.Desktop/Assets/avalonia-logo.ico"))),
            ToolTipText = "Second Brain",
            IsVisible = true,
            Menu = menu
        };
        trayIcon.Clicked += (_, _) => ShowMainWindow();

        TrayIcon.SetIcons(this, new TrayIcons { trayIcon });

        // Windows renderuje caly natywny tray-menu z gory (HMENU), wiec "Nowa notatka" wyglada
        // na wyszarzona/niedostepna dopoki podmenu ma 0 pozycji - samo "Opening" nie zdaza
        // wypelnic go na czas przy pierwszym otwarciu. Wypelniamy od razu i trzymamy w synchro
        // z drzewem folderow (Tree.CollectionChanged), Opening zostaje jako dodatkowy refresh.
        if (_mainWindow?.DataContext is MainViewModel vm)
            vm.Tree.CollectionChanged += (_, _) => RefreshNewNoteFolderMenu();

        RefreshNewNoteFolderMenu();
    }

    private void RefreshNewNoteFolderMenu()
    {
        if (_newNoteFolderMenu is null)
            return;

        _newNoteFolderMenu.Items.Clear();

        var folders = _mainWindow?.DataContext is MainViewModel vm
            ? vm.Tree.Where(t => t.IsFolder).Select(t => t.DisplayName).ToList()
            : [];

        if (folders.Count == 0)
        {
            _newNoteFolderMenu.Items.Add(new NativeMenuItem("(brak folderow)") { IsEnabled = false });
            return;
        }

        foreach (var folder in folders)
        {
            var folderMenu = new NativeMenu();

            var typeItem = new NativeMenuItem("Wpisz...");
            typeItem.Click += (_, _) => StartNewNoteInFolder(folder);
            folderMenu.Items.Add(typeItem);

            var clipboardItem = new NativeMenuItem("Ze schowka");
            clipboardItem.Click += async (_, _) => await NewNoteFromClipboardTextAsync(folder);
            folderMenu.Items.Add(clipboardItem);

            var imageItem = new NativeMenuItem("Obrazek ze schowka (OCR)");
            imageItem.Click += async (_, _) => await NewNoteFromClipboardImageAsync(folder);
            folderMenu.Items.Add(imageItem);

            _newNoteFolderMenu.Items.Add(new NativeMenuItem(folder) { Menu = folderMenu });
        }
    }

    private void StartNewNoteInFolder(string folder)
    {
        if (_mainWindow?.DataContext is not MainViewModel vm)
            return;

        ShowMainWindow();
        vm.SelectedFolder = folder;
        vm.SelectedTabIndex = MainViewModel.TabEditor;
    }

    // Tekst ze schowka (nie obrazek) trafia od razu do pola notatki, tak samo jak OCR ponizej -
    // user wciaz musi kliknac Zapisz, zeby dac szanse na poprawki przed kompresja/zapisem.
    private async Task NewNoteFromClipboardTextAsync(string folder)
    {
        if (_mainWindow?.DataContext is not MainViewModel vm)
            return;

        var clipboard = TopLevel.GetTopLevel(_mainWindow)?.Clipboard;
        var data = clipboard is null ? null : await clipboard.TryGetDataAsync();
        var textItem = data?.Items.FirstOrDefault(i => i.Formats.Contains(DataFormat.Text));
        if (textItem is null || await textItem.TryGetRawAsync(DataFormat.Text) is not string text || string.IsNullOrWhiteSpace(text))
            return;

        StartNewNoteInFolder(folder);
        vm.NoteText = text;
    }

    // Ten sam schowek->OCR co przycisk "Wklej obrazek ze schowka" w edytorze (patrz
    // MainWindow.axaml.cs PasteImage_Click), tylko wywolany z tray zamiast z okna.
    private async Task NewNoteFromClipboardImageAsync(string folder)
    {
        if (_mainWindow?.DataContext is not MainViewModel vm)
            return;

        var clipboard = TopLevel.GetTopLevel(_mainWindow)?.Clipboard;
        var data = clipboard is null ? null : await clipboard.TryGetDataAsync();
        var bitmapItem = data?.Items.FirstOrDefault(i => i.Formats.Contains(DataFormat.Bitmap));
        if (bitmapItem is null || await bitmapItem.TryGetRawAsync(DataFormat.Bitmap) is not Bitmap bitmap)
            return;

        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());

        StartNewNoteInFolder(folder);
        await vm.RunOcrCommand.ExecuteAsync(stream.ToArray());
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
            return;

        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Show();
        _mainWindow.Activate();
    }
}
