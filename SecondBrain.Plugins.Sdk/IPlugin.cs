using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SecondBrain.Plugins.Sdk;

// Plugin = funkcja aplikacji z warstwa backendowa (serwisy, magazyn, handlery zdarzen)
// i wizualna (zakladki, sloty, tray). Odkrywany refleksja (bezparametrowy konstruktor),
// wlaczany/wylaczany przez PluginManager. Wylaczony plugin nie dostaje ConfigureServices,
// wiec nic z niego nie trafia do DI - host nigdzie nie sprawdza "czy wlaczony".
public interface IPlugin
{
    // Stale, male litery, np. "gaps" - klucz w plugins.json.
    string Id { get; }
    string Name { get; }
    string Description { get; }

    // Wolane TYLKO dla wlaczonych pluginow, przed BuildServiceProvider.
    void ConfigureServices(IServiceCollection services, IConfiguration config);

    // Po starcie UI (okno otwarte, provider zbudowany) - stan poczatkowy, np. licznik na przycisku.
    Task StartAsync(IServiceProvider services, CancellationToken ct) => Task.CompletedTask;
}
