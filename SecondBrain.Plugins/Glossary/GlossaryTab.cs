using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using SecondBrain.Core;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Glossary;

// Lista ladowana przy kazdym wejsciu w zakladke - zapis notatki/akcja agenta dzieja sie
// w innych zakladkach, wiec to pokrywa dawne reczne LoadGlossaryAsync po kazdej mutacji.
public sealed partial class GlossaryTab(GlossaryStore store) : ObservableObject, ITabContribution
{
    public string Id => "glossary";
    public string Title => "Słownik";
    public TabArea Placement => TabArea.SidebarToolbar;
    public int Order => 40;
    public KeyGesture? Shortcut => null;

    public ObservableCollection<GlossaryEntry> GlossaryEntries { get; } = [];

    [ObservableProperty]
    public partial GlossaryEntry? SelectedGlossaryEntry { get; set; }

    [ObservableProperty]
    public partial bool HasGlossary { get; set; }

    public Control CreateView() => new GlossaryView();

    public async Task OnActivatedAsync(CancellationToken ct)
    {
        GlossaryEntries.Clear();
        foreach (var e in await store.ListAsync(ct))
            GlossaryEntries.Add(e);

        HasGlossary = GlossaryEntries.Count > 0;
    }
}
