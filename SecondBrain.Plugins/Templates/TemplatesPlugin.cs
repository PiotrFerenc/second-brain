using Avalonia.Controls;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Core;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Templates;

// Szablony notatek: rzad przyciskow nad edytorem, klik wkleja tresc szablonu do pola.
// Magazyn .templates/ (TemplateStore) i narzedzie list_templates tez tu - wylaczony plugin nie ma zadnego z nich.
public sealed class TemplatesPlugin : IPlugin
{
    public string Id => "templates";
    public string Name => "Szablony";
    public string Description => "Przyciski szablonów nad edytorem nowej notatki.";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<TemplateStore>();
        services.AddSingleton<ISlotContribution, TemplatesSlot>();
        services.AddSingleton<IAgentTool, ListTemplatesTool>();
    }
}

// Lista szablonow czytana raz przy budowie kontrolki (tak jak host robil to raz przy starcie) -
// nowy plik w .templates/ wymaga restartu, jak dotad.
public sealed class TemplatesSlot(TemplateStore templates, IEditorContext editor) : ISlotContribution
{
    public string SlotId => "Editor.Templates";
    public int Order => 0;

    public Control CreateControl()
    {
        var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        panel.AttachedToVisualTree += async (_, _) =>
        {
            if (panel.Children.Count > 0)
                return;
            foreach (var template in await templates.ListAsync())
            {
                var button = new Button { Content = template.Name, Classes = { "subtleAction" } };
                button.Click += (_, _) => editor.Text = template.Content;
                panel.Children.Add(button);
            }
        };
        return panel;
    }
}
