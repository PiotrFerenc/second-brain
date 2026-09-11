using System.Collections.ObjectModel;

namespace SecondBrain.Desktop.ViewModels;

// Wezel drzewa w sidebarze: folder (korzen) albo notatka (lisc/gniazdo dla podstron).
// OwningFolder pozwala na kazdym poziomie zaglebienia wiedziec, do ktorego folderu
// (kolekcji Qdrant) notatka nalezy, bez wspinania sie po drzewie w gore.
public class TreeItem
{
    public required string DisplayName { get; init; }
    public required bool IsFolder { get; init; }
    public required string OwningFolder { get; init; }
    public SearchResultItem? Note { get; init; }
    public ObservableCollection<TreeItem> Children { get; } = [];
}
