using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Core;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Tasks;

// Zadania: zbiorcza lista "- [ ] ..." ze wszystkich notatek z terminami (RRRR-MM-DD w tekscie).
// Bez wlasnego magazynu - notatki sa zrodlem prawdy; wylaczony plugin = brak zakladki i narzedzia.
public sealed class TasksPlugin : IPlugin
{
    public string Id => "tasks";
    public string Name => "Zadania";
    public string Description => "Zbiorcza lista zadań ('- [ ]') ze wszystkich notatek, z terminami i oznaczeniem przeterminowanych.";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<TasksTab>();
        services.AddSingleton<ITabContribution>(sp => sp.GetRequiredService<TasksTab>());
        services.AddSingleton<IEventHandler<NotesChanged>>(sp => sp.GetRequiredService<TasksTab>());
        services.AddSingleton<IAgentTool, ListTasksTool>();
    }

    public Task StartAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<TasksTab>().LoadAsync(ct);
}
