using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Core;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Gaps;

// Luki w wiedzy: pytania, na ktore RAG jawnie odpowiedzial "notatki tego nie zawieraja",
// trzymane jako trwaly backlog rzeczy do dopisania. Magazyn (.gaps/) zostaje w INoteStore,
// bo agent (list_gaps/resolve_gap) tez z niego korzysta - patrz PLAN-AGENT-PLUGINS.md P2.
public sealed class GapsPlugin : IPlugin
{
    public string Id => "gaps";
    public string Name => "Luki w wiedzy";
    public string Description => "Pytania bez odpowiedzi w notatkach jako lista do uzupełnienia; auto-domykanie po dodaniu pasującej notatki.";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<GapAutoCloser>();
        services.AddSingleton<GapsTab>();
        services.AddSingleton<ITabContribution>(sp => sp.GetRequiredService<GapsTab>());
        services.AddSingleton<IEventHandler<StorageChanged>>(sp => sp.GetRequiredService<GapsTab>());
        services.AddSingleton<IEventHandler<SearchCompleted>, GapLogOnSearch>();
        services.AddSingleton<IEventHandler<NoteAdded>, GapAutoCloseOnNoteAdded>();
        services.AddSingleton<IEventHandler<ImportCompleted>, GapAutoCloseOnNoteAdded>();
    }

    public Task StartAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<GapsTab>().LoadGapsCommand.ExecuteAsync(null);
}
