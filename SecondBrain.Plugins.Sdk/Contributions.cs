using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;

namespace SecondBrain.Plugins.Sdk;

// Kontrybucje UI to zwykle rejestracje DI (services.AddSingleton<ITabContribution, X>()) -
// host zbiera je przez IEnumerable<...>. Jeden mechanizm dla backendu i UI.

// "TabArea", nie "TabPlacement" - Avalonia.Controls ma juz TabPlacement i kazdy plugin z `using Avalonia.Controls` by sie o to rozbil.
public enum TabArea { SidebarToolbar, ContentHeader, Hidden }

public interface ITabContribution : INotifyPropertyChanged
{
    string Id { get; }                  // cel dla IShell.ShowTab
    string Title { get; }               // bindowalne: "Luki (3)" aktualizuje przycisk
    TabArea Placement { get; }
    int Order { get; }
    KeyGesture? Shortcut { get; }       // np. Ctrl+F dla search
    Control CreateView();               // raz, host cache'uje i ustawia DataContext = this
    Task OnActivatedAsync(CancellationToken ct) => Task.CompletedTask;
}

// Fragment wstawiany w slot istniejacego widoku hosta (patrz SlotHost i lista slotow
// w PLAN-PLUGINS.md 2.5). Wlasny DataContext; stan hosta przez IShell/IEditorContext.
public interface ISlotContribution
{
    string SlotId { get; }
    int Order { get; }
    Control CreateControl();
}

// Pozycja w podmenu tray "Nowa notatka > <folder>".
public interface ITrayNewNoteContribution
{
    NativeMenuItem Build(string folder);
}
