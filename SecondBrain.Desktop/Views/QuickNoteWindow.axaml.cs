using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SecondBrain.Desktop.ViewModels;

namespace SecondBrain.Desktop.Views;

// Male okienko "szybka notatka" jak w OneNote (Win+N) - wpisujesz i Zapisz/Ctrl+Enter,
// zamiast otwierac cale glowne okno. Dzieli DataContext (MainViewModel) z MainWindow,
// wiec Zapisz idzie przez ten sam pipeline (kompresja/embedding/tagi) co edytor w glownym oknie.
public partial class QuickNoteWindow : Window
{
    public QuickNoteWindow()
    {
        InitializeComponent();
        Opened += (_, _) => NoteTextBox.Focus();
        KeyDown += OnKeyDown;
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
            await SaveAsync();
        else if (e.Key == Key.Escape)
            Close();
    }

    private async void Save_Click(object? sender, RoutedEventArgs e) => await SaveAsync();

    private async Task SaveAsync()
    {
        if (DataContext is not MainViewModel vm)
            return;

        await vm.SaveNoteCommand.ExecuteAsync(null);

        if (vm.EditorStatus.StartsWith("Zapisano"))
            Close();
    }
}
