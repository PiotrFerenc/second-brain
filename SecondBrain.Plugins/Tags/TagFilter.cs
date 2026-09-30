using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Tags;

// Aktywny filtr po tagu: zaweza drzewo do notatek majacych ten tag (ze wszystkich folderow),
// dopoki nie zostanie wyczyszczony. Wspolny stan chipow (ustawiaja) i banera (pokazuje/czysci).
public sealed partial class TagFilter(IShell shell) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    public partial string? ActiveTag { get; set; }

    public bool IsActive => ActiveTag is not null;

    public async Task ApplyAsync(string tag)
    {
        ActiveTag = tag;
        shell.TreeFilter = n => n.Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
        await shell.RefreshTreeAsync();
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        ActiveTag = null;
        shell.TreeFilter = null;
        await shell.RefreshTreeAsync();
    }
}

// Baner "tag: x" z przyciskiem "x" nad drzewem folderow.
public sealed class TagFilterBanner(TagFilter filter) : ISlotContribution
{
    public string SlotId => "Sidebar.AboveTree";
    public int Order => 0;

    public Control CreateControl()
    {
        var label = new TextBlock { Classes = { "subtle" }, FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var value = new Run();
        value.Bind(Run.TextProperty, new Binding(nameof(TagFilter.ActiveTag)));
        label.Inlines!.Add(new Run("tag: "));
        label.Inlines.Add(value);

        var clear = new Button { Content = "×", Classes = { "subtleAction" }, Padding = new Avalonia.Thickness(6, 0), Command = filter.ClearCommand };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(clear, 1);
        grid.Children.Add(label);
        grid.Children.Add(clear);

        var border = new Border
        {
            DataContext = filter,
            Child = grid,
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(8, 6),
            Margin = new Avalonia.Thickness(0, 0, 0, 10),
        };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("CtpSurface0"));
        border.Bind(Avalonia.Visual.IsVisibleProperty, new Binding(nameof(TagFilter.IsActive)));
        return border;
    }
}

// Chipy tagow wybranej notatki pod jej tytulem; klik = filtr drzewa po tym tagu.
public sealed class TagChipsSlot(IShell shell, TagFilter filter) : ISlotContribution
{
    public string SlotId => "Note.Header";
    public int Order => 0;

    public Control CreateControl()
    {
        var panel = new WrapPanel();
        Rebuild(panel);
        shell.SelectedNoteChanged += () => Rebuild(panel);
        return panel;
    }

    private void Rebuild(WrapPanel panel)
    {
        panel.Children.Clear();
        foreach (var tag in shell.SelectedNote?.TagList ?? [])
        {
            var button = new Button { Content = tag, Classes = { "subtleAction" } };
            button.Click += async (_, _) => await filter.ApplyAsync(tag);
            panel.Children.Add(button);
        }
    }
}
