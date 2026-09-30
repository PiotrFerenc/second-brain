using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Core;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.History;

// Katalog notatek jako repo gita: cichy commit po kazdej zmianie na dysku (backend)
// + zakladka "Historia" z lista commitow i diffem (UI). Wylaczony = brak jednego i drugiego.
public sealed class HistoryPlugin : IPlugin
{
    public string Id => "history";
    public string Name => "Historia";
    public string Description => "Automatyczny backup notatek do gita i zakładka z historią zmian.";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<GitRepository>();
        services.AddSingleton<IEventHandler<StorageChanged>, GitCommitOnChange>();
        services.AddSingleton<ITabContribution, HistoryTab>();
    }
}

// Cichy backup do gita po kazdej zmianie na dysku (patrz GitRepository.ScheduleCommit).
public sealed class GitCommitOnChange(GitRepository git) : IEventHandler<StorageChanged>
{
    public Task HandleAsync(StorageChanged e, CancellationToken ct = default)
    {
        git.ScheduleCommit(e.Message);
        return Task.CompletedTask;
    }
}
