using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.QuickNote;

// Male okienko "szybka notatka" jak w OneNote (Win+N) - wpisujesz i Zapisz/Ctrl+Enter,
// zamiast otwierac cale glowne okno. DataContext to IEditorContext hosta, wiec Zapisz idzie
// przez ten sam pipeline (kompresja/embedding/tagi) co edytor w glownym oknie.
public partial class QuickNoteWindow : Window
{
    // Bezparametrowy konstruktor wymaga go loader XAML (AVLN3001); kontekst wchodzi przez DataContext.
    public QuickNoteWindow()
    {
        InitializeComponent();
        Opened += (_, _) => NoteTextBox.Focus();
        KeyDown += OnKeyDown;
    }

    private IEditorContext Editor => (IEditorContext)DataContext!;

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
        await Editor.SaveAsync();

        if (Editor.Status.StartsWith("Zapisano"))
            Close();
    }
}
