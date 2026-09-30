using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Core;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Glossary;

// Auto-slownik: definicje ("X to Y") zlapane przy kompresji notatki trafiaja do .glossary/,
// zakladka je listuje. Magazyn na razie przez INoteStore (uzywa go tez agent) - wlasny
// GlossaryStore dopiero w fazie 2 (PLAN-PLUGINS.md).
public sealed class GlossaryPlugin : IPlugin
{
    public string Id => "glossary";
    public string Name => "Słownik";
    public string Description => "Auto-słownik pojęć wyłapanych przy zapisie notatek.";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<ITabContribution, GlossaryTab>();
        services.AddSingleton<GlossaryOnNoteAdded>();
        services.AddSingleton<IEventHandler<NoteAdded>>(sp => sp.GetRequiredService<GlossaryOnNoteAdded>());
        services.AddSingleton<IEventHandler<NoteEdited>>(sp => sp.GetRequiredService<GlossaryOnNoteAdded>());
    }
}
