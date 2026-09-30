using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Microsoft.Extensions.DependencyInjection;

namespace SecondBrain.Plugins.Sdk;

// <sdk:SlotHost SlotId="Editor.Toolbar" /> w XAML hosta: po dolaczeniu do drzewa wizualnego
// pobiera ISlotContribution o tym SlotId, sortuje po Order i tworzy kontrolki raz.
// Pusty slot (brak kontrybucji) nie renderuje nic.
public class SlotHost : ItemsControl
{
    public static readonly StyledProperty<string?> SlotIdProperty =
        AvaloniaProperty.Register<SlotHost, string?>(nameof(SlotId));

    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<SlotHost, Orientation>(nameof(Orientation), Orientation.Horizontal);

    public string? SlotId
    {
        get => GetValue(SlotIdProperty);
        set => SetValue(SlotIdProperty, value);
    }

    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    // Bez tego podklasa nie dostaje motywu ItemsControl i nic sie nie renderuje.
    protected override Type StyleKeyOverride => typeof(ItemsControl);

    public SlotHost()
    {
        ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation, Spacing = 8 });
    }

    // AttachedToVisualTree nie odpala dla kontrolek z pierwszego layoutu okna - wypelniamy tez
    // przy ustawieniu SlotId (parsowanie XAML), a attach zostaje jako fallback gdy Services jeszcze brak.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SlotIdProperty)
            Fill();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Fill();
    }

    private void Fill()
    {
        if (ItemsSource is not null || SlotId is null || PluginRuntime.Services is null)
            return;

        ItemsSource = PluginRuntime.Services.GetServices<ISlotContribution>()
            .Where(c => c.SlotId == SlotId)
            .OrderBy(c => c.Order)
            .Select(c => c.CreateControl())
            .ToList();
    }
}
