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
    // Compression/AnswerSynthesis/ConflictDetection/Agent/TagCleaning/Embedding moga wiec
    // wskazywac na rozne adresy/klucze, nawet jesli dzis wszystkie mierza w ta sama infrastrukture.
    // Qdrant to jedno wspolne polaczenie do bazy wektorowej, nie provider LLM - zostaje jak jest.
    public static IServiceCollection AddSecondBrainInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<CompressionOptions>(config.GetSection("Compression"));
        services.Configure<AnswerSynthesisOptions>(config.GetSection("AnswerSynthesis"));
        services.Configure<ConflictDetectionOptions>(config.GetSection("ConflictDetection"));
        services.Configure<AgentOptions>(config.GetSection("Agent"));
        services.Configure<TagCleaningOptions>(config.GetSection("TagCleaning"));
        services.Configure<EmbeddingOptions>(config.GetSection("Embedding"));
        services.Configure<RerankerOptions>(config.GetSection("Reranker"));
        services.Configure<QdrantOptions>(config.GetSection("Qdrant"));
        services.Configure<StorageOptions>(config.GetSection("Storage"));
        services.Configure<OcrOptions>(config.GetSection("Ocr"));

        services.AddHttpClient("Compression", (sp, client) =>
            HttpClientHeaders.Apply(client, sp.GetRequiredService<IOptions<CompressionOptions>>().Value));

        services.AddHttpClient("AnswerSynthesis", (sp, client) =>
            HttpClientHeaders.Apply(client, sp.GetRequiredService<IOptions<AnswerSynthesisOptions>>().Value));

        services.AddHttpClient("ConflictDetection", (sp, client) =>
            HttpClientHeaders.Apply(client, sp.GetRequiredService<IOptions<ConflictDetectionOptions>>().Value));

        services.AddHttpClient("Agent", (sp, client) =>
            HttpClientHeaders.Apply(client, sp.GetRequiredService<IOptions<AgentOptions>>().Value));

        services.AddHttpClient("TagCleaning", (sp, client) =>
            HttpClientHeaders.Apply(client, sp.GetRequiredService<IOptions<TagCleaningOptions>>().Value));

        services.AddHttpClient("Embedding", (sp, client) =>
            HttpClientHeaders.Apply(client, sp.GetRequiredService<IOptions<EmbeddingOptions>>().Value));

        services.AddHttpClient("Reranker", (sp, client) =>
            HttpClientHeaders.Apply(client, sp.GetRequiredService<IOptions<RerankerOptions>>().Value));

        services.AddHttpClient("Ocr", (sp, client) =>
            HttpClientHeaders.Apply(client, sp.GetRequiredService<IOptions<OcrOptions>>().Value));

        services.AddSingleton<IEmbedder, MockEmbedder>();
        services.AddSingleton<IReranker, MockReranker>();
        services.AddSingleton<ICompressor, FabrykaCompressor>();
        services.AddSingleton<IAnswerSynthesizer, FabrykaAnswerSynthesizer>();
        services.AddSingleton<IConflictDetector, FabrykaConflictDetector>();
        services.AddSingleton<IAgent, FabrykaAgent>();
        services.AddSingleton<GapAutoCloser>();
        services.AddSingleton<ITagCleaner, FabrykaTagCleaner>();
        services.AddSingleton<TagMerger>();
        services.AddSingleton<IOcrExtractor, LightOnOcrExtractor>();
        services.AddSingleton<INoteStore>(sp => new GitBackedNoteStore(
            new FileNoteStore(sp.GetRequiredService<IOptions<StorageOptions>>()),
            sp.GetRequiredService<IOptions<StorageOptions>>()));
        services.AddSingleton<IVectorIndex, QdrantVectorIndex>();
        services.AddSingleton<DuplicateScanner>();

        return services;
    }
}
