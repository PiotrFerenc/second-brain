using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Core;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Glossary;

// Auto-slownik: definicje ("X to Y") zlapane przy kompresji notatki trafiaja do .glossary/,
// zakladka je listuje, agent ma do nich narzedzia. Wlasny magazyn (GlossaryStore) - nic z tego
// nie zyje w INoteStore.
public sealed class GlossaryPlugin : IPlugin
{
    public string Id => "glossary";
    public string Name => "Słownik";
    public string Description => "Auto-słownik pojęć wyłapanych przy zapisie notatek.";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<GlossaryStore>();
        services.AddSingleton<ITabContribution, GlossaryTab>();
        services.AddSingleton<IAgentTool, ListGlossaryTool>();
        services.AddSingleton<IAgentTool, AddGlossaryEntryTool>();
        services.AddSingleton<IAgentTool, DeleteGlossaryEntryTool>();
        services.AddSingleton<GlossaryOnNoteAdded>();
        services.AddSingleton<IEventHandler<NoteAdded>>(sp => sp.GetRequiredService<GlossaryOnNoteAdded>());
        services.AddSingleton<IEventHandler<NoteEdited>>(sp => sp.GetRequiredService<GlossaryOnNoteAdded>());
    }
}
