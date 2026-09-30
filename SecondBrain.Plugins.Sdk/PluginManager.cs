using System.Reflection;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SecondBrain.Plugins.Sdk;

// Odkrywanie pluginow (refleksja po stalej liscie asemblerow), stan wlaczenia w
// ~/SecondBrain/plugins.json ({"disabled":[...]} - lista WYLACZONYCH, zeby nowy plugin byl
// domyslnie widoczny), ConfigureServices/StartAsync tylko dla wlaczonych. Zmiana stanu
// dziala po restarcie - kontener DI jest niezmienny po zbudowaniu (patrz PLAN-PLUGINS.md 4).
// W SDK (nie w Desktop), bo CLI tez laduje backend pluginow.
public sealed class PluginManager
{
    // Obok window.json - lokalny stan uzytkownika, nie konfiguracja providerow.
    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SecondBrain", "plugins.json");

    private record State(string[] Disabled);

    private readonly List<PluginEntry> _entries = [];

    public IReadOnlyList<PluginEntry> Plugins => _entries;

    private IEnumerable<IPlugin> Enabled => _entries.Where(e => e.IsEnabled).Select(e => e.Plugin);

    public static PluginManager Discover(params Assembly[] assemblies)
    {
        var manager = new PluginManager();
        var disabled = LoadDisabled();

        var plugins = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IPlugin).IsAssignableFrom(t))
            .Select(t => (IPlugin)Activator.CreateInstance(t)!)
            .OrderBy(p => p.Id, StringComparer.Ordinal);

        foreach (var plugin in plugins)
            manager._entries.Add(new PluginEntry(plugin, !disabled.Contains(plugin.Id), manager.Save));

        return manager;
    }

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton(this);
        foreach (var plugin in Enabled)
            plugin.ConfigureServices(services, config);
    }

    public async Task StartAsync(IServiceProvider services, CancellationToken ct = default)
    {
        foreach (var plugin in Enabled)
            await plugin.StartAsync(services, ct);
    }

    public void SetEnabled(string id, bool enabled)
    {
        var entry = _entries.FirstOrDefault(e => e.Id == id);
        if (entry is not null)
            entry.IsEnabled = enabled;   // OnIsEnabledChanged -> Save
    }

    private static HashSet<string> LoadDisabled()
    {
        try
        {
            if (!File.Exists(StatePath))
                return [];
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath), JsonOptions);
            return new HashSet<string>(state?.Disabled ?? [], StringComparer.Ordinal);
        }
        catch
        {
            // Uszkodzony plik = wszystko wlaczone; nadpisze sie przy nastepnej zmianie.
            return [];
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            var state = new State(_entries.Where(e => !e.IsEnabled).Select(e => e.Id).ToArray());
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch
        {
            // Best-effort jak window.json - brak zapisu nie moze wywrocic aplikacji.
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}

// Wiersz w menedzerze wtyczek - IsEnabled bindowalne do ToggleSwitch, kazda zmiana od razu zapisana.
public sealed partial class PluginEntry(IPlugin plugin, bool enabled, Action save) : ObservableObject
{
    internal IPlugin Plugin { get; } = plugin;

    public string Id => Plugin.Id;
    public string Name => Plugin.Name;
    public string Description => Plugin.Description;

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = enabled;

    partial void OnIsEnabledChanged(bool value) => save();
}
