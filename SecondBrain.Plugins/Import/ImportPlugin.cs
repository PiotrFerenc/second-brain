using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Import;

// Import zbiorczy do edytora: z pliku (kazda linia = notatka) i z folderu (kazdy .txt/.md = notatka).
// Sam import robi NotePipeline.ImportAsync (rdzen) - plugin to tylko dwa przyciski w pasku edytora.
public sealed class ImportPlugin : IPlugin
{
    public string Id => "import";
    public string Name => "Import";
    public string Description => "Import notatek z pliku tekstowego (linia = notatka) lub z folderu (plik = notatka).";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<ISlotContribution, ImportFileAction>();
        services.AddSingleton<ISlotContribution, ImportFolderAction>();
    }
}
