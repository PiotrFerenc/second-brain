using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Core;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Timeline;

// Os czasu: wszystkie notatki ze wszystkich folderow pogrupowane po dacie utworzenia.
public sealed class TimelinePlugin : IPlugin
{
    public string Id => "timeline";
    public string Name => "Oś czasu";
    public string Description => "Zakładka z notatkami ze wszystkich folderów pogrupowanymi po dacie utworzenia.";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<TimelineTab>();
        services.AddSingleton<ITabContribution>(sp => sp.GetRequiredService<TimelineTab>());
        services.AddSingleton<IEventHandler<NotesChanged>>(sp => sp.GetRequiredService<TimelineTab>());
    }
}
