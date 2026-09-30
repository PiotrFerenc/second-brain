using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Core;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Search;

// Szukaj: zakladka z wyszukiwaniem hybrydowym (NoteSearch z rdzenia) i karta odpowiedzi RAG.
// Publikuje SearchCompleted (luki w wiedzy nasluchuja). Sam NoteSearch zostaje w rdzeniu -
// uzywa go tez agent, CLI i auto-domykanie luk; wylaczenie pluginu zabiera tylko UI.
public sealed class SearchPlugin : IPlugin
{
    public string Id => "search";
    public string Name => "Szukaj";
    public string Description => "Wyszukiwanie semantyczne po notatkach z odpowiedzią LLM (RAG), Ctrl+F.";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<SearchTab>();
        services.AddSingleton<ITabContribution>(sp => sp.GetRequiredService<SearchTab>());
        services.AddSingleton<IAgentTool, SearchNotesTool>();
        services.AddSingleton<IAgentTool, AskQuestionTool>();
    }
}
