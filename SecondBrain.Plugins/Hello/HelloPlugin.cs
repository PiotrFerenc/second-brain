using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Hello;

// Plugin-probka: dowod, ze odkrywanie, menedzer i zakladka z XAML w tym asemblerze dzialaja.
// Do usuniecia w fazie 2 (PLAN-PLUGINS.md), gdy beda prawdziwe pluginy.
public sealed class HelloPlugin : IPlugin
{
    public string Id => "hello";
    public string Name => "Hello";
    public string Description => "Plugin-próbka: jedna zakładka z tekstem.";

    public void ConfigureServices(IServiceCollection services, IConfiguration config) =>
        services.AddSingleton<ITabContribution, HelloTab>();
}
