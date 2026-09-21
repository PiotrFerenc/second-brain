using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SecondBrain.Desktop.ViewModels;

// Wezel drzewa w sidebarze: folder (korzen) albo notatka (lisc/gniazdo dla podstron).
// OwningFolder pozwala na kazdym poziomie zaglebienia wiedziec, do ktorego folderu
// (pliku indeksu wektorowego) notatka nalezy, bez wspinania sie po drzewie w gore.
public partial class TreeItem : ObservableObject
{
    public required string DisplayName { get; init; }
    public required bool IsFolder { get; init; }
    public required string OwningFolder { get; init; }
    public SearchResultItem? Note { get; init; }
    public ObservableCollection<TreeItem> Children { get; } = [];

    // LoadTreeAsync buduje drzewo od zera przy kazdym odswiezeniu (nowe instancje TreeItem),
    // wiec stan rozwiniecia trzeba jawnie przeniesc ze starych wezlow na nowe po kluczu
    // (folder/note id) - patrz CollectExpandedKeys/ApplyExpandedKeys w MainViewModel.
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }
}
