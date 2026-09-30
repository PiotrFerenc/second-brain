using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Core;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Duplicates;

// Wykrywanie duplikatow miedzyfolderowych. Bez UI: skan dostepny agentowi (find_duplicate_notes)
// i z CLI (find-duplicates); scalanie/kasowanie to swiadomy reczny follow-up.
public sealed class DuplicatesPlugin : IPlugin
{
    public string Id => "duplicates";
    public string Name => "Duplikaty";
    public string Description => "Znajduje niemal identyczne notatki leżące w różnych folderach.";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<DuplicateScanner>();
        services.AddSingleton<IAgentTool, FindDuplicateNotesTool>();
    }
}
