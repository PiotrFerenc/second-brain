using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Core;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Trash;

// Kosz: zakladka z lista usunietych notatek (przywroc / usun na zawsze) oraz przycisk "Usun"
// w widoku notatki i przy wyniku wyszukiwania. Sam magazyn kosza (.trash) zostaje w INoteStore -
// uzywa go tez agent i CLI, wylaczenie pluginu zabiera tylko UI.
public sealed class TrashPlugin : IPlugin
{
    public string Id => "trash";
    public string Name => "Kosz";
    public string Description => "Usuwanie notatek do kosza, przywracanie i trwałe usuwanie.";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        // Jedna instancja zakladki jako kontrybucja i jako handler zdarzen (lista odswieza sie
        // takze po akcjach agenta/CLI, ktore nie przechodza przez jej komendy).
        services.AddSingleton<TrashTab>();
        services.AddSingleton<ITabContribution>(sp => sp.GetRequiredService<TrashTab>());
        services.AddSingleton<IEventHandler<NoteTrashed>>(sp => sp.GetRequiredService<TrashTab>());
        services.AddSingleton<IEventHandler<NoteRestored>>(sp => sp.GetRequiredService<TrashTab>());
        services.AddSingleton<IEventHandler<NotePurged>>(sp => sp.GetRequiredService<TrashTab>());

        services.AddSingleton<ISlotContribution, DeleteNoteAction>();
        services.AddSingleton<ISlotContribution, DeleteSearchResultAction>();

        services.AddSingleton<IAgentTool, TrashNoteTool>();
        services.AddSingleton<IAgentTool, ListTrashTool>();
        services.AddSingleton<IAgentTool, RestoreNoteTool>();
        services.AddSingleton<IAgentTool, PurgeNoteTool>();
        services.AddSingleton<IAgentTool, PurgeTrashAllTool>();
        services.AddSingleton<IAgentTool, BulkRestoreTool>();
        services.AddSingleton<IAgentTool, BulkPurgeTool>();
        services.AddSingleton<IAgentTool, BulkTrashTool>();
    }
}
