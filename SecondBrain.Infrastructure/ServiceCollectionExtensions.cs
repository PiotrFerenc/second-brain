using SecondBrain.Infrastructure.AgentTools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

public static class ServiceCollectionExtensions
{
    // Domyslne rejestracje: MockEmbedder + MockReranker (patrz PLAN.md, sekcja decyzji -
    // embeddingi zablokowane po stronie providera, realny reranker dostepny tylko
    // na drugiej maszynie). Wywolujacy moze nadpisac pojedyncza rejestracje po tym wywolaniu
    // (np. services.AddSingleton&lt;IEmbedder, FabrykaEmbedder&gt;()) - ostatnia wygrywa.
    //
    // Kazdy provider strzelajacy do API ma wlasna sekcje configu i wlasny named HttpClient -
    // Compression/AnswerSynthesis/Agent/Embedding moga wiec
    // wskazywac na rozne adresy/klucze, nawet jesli dzis wszystkie mierza w ta sama infrastrukture.
    // VectorIndex to lokalny plikowy magazyn wektorow (FileVectorIndex), nie provider LLM - zostaje jak jest.
    public static IServiceCollection AddSecondBrainInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<CompressionOptions>(config.GetSection("Compression"));
        services.Configure<AnswerSynthesisOptions>(config.GetSection("AnswerSynthesis"));
        services.Configure<AgentOptions>(config.GetSection("Agent"));
        services.Configure<EmbeddingOptions>(config.GetSection("Embedding"));
        services.Configure<RerankerOptions>(config.GetSection("Reranker"));
        services.Configure<VectorIndexOptions>(config.GetSection("VectorIndex"));
        services.Configure<StorageOptions>(config.GetSection("Storage"));
        services.Configure<PluginsOptions>(config.GetSection("Plugins"));

        services.AddHttpClient("Compression", (sp, client) =>
            HttpClientHeaders.Apply(client, sp.GetRequiredService<IOptions<CompressionOptions>>().Value));

        services.AddHttpClient("AnswerSynthesis", (sp, client) =>
            HttpClientHeaders.Apply(client, sp.GetRequiredService<IOptions<AnswerSynthesisOptions>>().Value));

        services.AddHttpClient("Agent", (sp, client) =>
            HttpClientHeaders.Apply(client, sp.GetRequiredService<IOptions<AgentOptions>>().Value));

        services.AddHttpClient("Embedding", (sp, client) =>
            HttpClientHeaders.Apply(client, sp.GetRequiredService<IOptions<EmbeddingOptions>>().Value));

        services.AddHttpClient("Reranker", (sp, client) =>
            HttpClientHeaders.Apply(client, sp.GetRequiredService<IOptions<RerankerOptions>>().Value));

        services.AddSingleton<IEmbedder, MockEmbedder>();
        services.AddSingleton<IReranker, MockReranker>();
        services.AddSingleton<ICompressor, FabrykaCompressor>();
        services.AddSingleton<IAnswerSynthesizer, FabrykaAnswerSynthesizer>();
        services.AddSingleton<IAgent, FabrykaAgent>();

        // Narzedzia agenta: kazda konkretna klasa IAgentTool z tego asemblera (patrz AgentTools/),
        // plus rejestr sklejajacy je ze zrodlami runtime (MCP). Pluginy dokladaja swoje przez DI.
        foreach (var toolType in typeof(ServiceCollectionExtensions).Assembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && typeof(IAgentTool).IsAssignableFrom(t)))
            services.AddSingleton(typeof(IAgentTool), toolType);
        services.AddSingleton<AgentToolRegistry>();
        services.AddSingleton<IAgentToolSource, McpToolSource>();
        services.AddSingleton<NotesRoot>();
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<INoteStore, FileNoteStore>();
        services.AddSingleton<NoteSearch>();
        services.AddSingleton<NotePipeline>();

        services.AddSingleton<IVectorIndex, FileVectorIndex>();
        services.AddSingleton<IAgentSessionStore, FileAgentSessionStore>();

        return services;
    }
}
