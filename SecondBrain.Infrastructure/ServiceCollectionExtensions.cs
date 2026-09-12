using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

public static class ServiceCollectionExtensions
{
    // Domyslne rejestracje: MockEmbedder + MockReranker (patrz PLAN.md, sekcja decyzji -
    // OpenAI embeddings zablokowane po stronie providera, realny reranker dostepny tylko
    // na drugiej maszynie). Wywolujacy moze nadpisac pojedyncza rejestracje po tym wywolaniu
    // (np. services.AddSingleton&lt;IEmbedder, OpenAiEmbedder&gt;()) - ostatnia wygrywa.
    public static IServiceCollection AddSecondBrainInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<OpenAiOptions>(config.GetSection("OpenAI"));
        services.Configure<RerankerOptions>(config.GetSection("Reranker"));
        services.Configure<QdrantOptions>(config.GetSection("Qdrant"));
        services.Configure<StorageOptions>(config.GetSection("Storage"));

        services.AddHttpClient("OpenAI", (sp, client) =>
            HttpClientHeaders.Apply(client, sp.GetRequiredService<IOptions<OpenAiOptions>>().Value));

        services.AddHttpClient("Reranker", (sp, client) =>
            HttpClientHeaders.Apply(client, sp.GetRequiredService<IOptions<RerankerOptions>>().Value));

        services.AddSingleton<IEmbedder, MockEmbedder>();
        services.AddSingleton<IReranker, MockReranker>();
        services.AddSingleton<ICompressor, OpenAiCompressor>();
        services.AddSingleton<IAnswerSynthesizer, OpenAiAnswerSynthesizer>();
        services.AddSingleton<IConflictDetector, OpenAiConflictDetector>();
        services.AddSingleton<IAgent, OpenAiAgent>();
        services.AddSingleton<INoteStore, FileNoteStore>();
        services.AddSingleton<IVectorIndex, QdrantVectorIndex>();

        return services;
    }
}
