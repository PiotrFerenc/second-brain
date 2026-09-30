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

    public SlotHost()
    {
        ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation, Spacing = 8 });
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (ItemsSource is not null || SlotId is null || PluginRuntime.Services is null)
            return;

        ItemsSource = PluginRuntime.Services.GetServices<ISlotContribution>()
            .Where(c => c.SlotId == SlotId)
            .OrderBy(c => c.Order)
            .Select(c => c.CreateControl())
            .ToList();
    }
}
