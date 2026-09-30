using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Desktop.ViewModels;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Desktop.Plugins;

public static class ShellServices
{
    // Rdzen UI: jeden MainViewModel na proces (singleton - adaptery i okna dziela instancje),
    // adaptery IShell/IEditorContext dla pluginow, zakladka "Wtyczki".
    public static IServiceCollection AddSecondBrainShell(this IServiceCollection services)
    {
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<IShell, ShellAdapter>();
        services.AddSingleton<IEditorContext, EditorContextAdapter>();
        services.AddSingleton<ITabContribution, PluginsTab>();
        return services;
    }
}
