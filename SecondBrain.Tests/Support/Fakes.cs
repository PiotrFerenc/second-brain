using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecondBrain.Core;
using SecondBrain.Infrastructure;

namespace SecondBrain.Tests.Support;

// Katalog notatek w /tmp, sprzatany razem z testem (xunit tworzy instancje klasy na kazdy test).
public sealed class TempRoot : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "secondbrain-tests-" + Guid.NewGuid().ToString("N"));

    public TempRoot() => Directory.CreateDirectory(Path);

    public NotesRoot Notes => new(Options.Create(new StorageOptions { NotesRootPath = Path }));

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
    }
}

public sealed class RecordingEventBus : IEventBus
{
    public List<object> Events { get; } = [];

    public Task PublishAsync<TEvent>(TEvent e, CancellationToken ct = default)
    {
        Events.Add(e!);
        return Task.CompletedTask;
    }

    public IEnumerable<T> Of<T>() => Events.OfType<T>();
}

public sealed class FakeCompressor : ICompressor
{
    public List<string> Inputs { get; } = [];
    public Func<string, CompressionResult>? Factory { get; set; }

    public Task<CompressionResult> CompressAsync(string rawText, CancellationToken ct = default)
    {
        Inputs.Add(rawText);
        var result = Factory?.Invoke(rawText)
            ?? new CompressionResult($"T:{rawText}", $"C:{rawText}", ["tag"], []);
        return Task.FromResult(result);
    }
}

// Wektor zalezny od tekstu, ale prosty do przewidzenia: mapa slowo -> wektor, reszta = deterministyczny hash.
public sealed class FakeEmbedder : IEmbedder
{
    public Dictionary<string, float[]> Map { get; } = new();
    public List<string> Inputs { get; } = [];

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        Inputs.Add(text);
        if (Map.TryGetValue(text, out var v))
            return Task.FromResult(v);

        var hash = (float)(Math.Abs(text.GetHashCode()) % 97 + 1);
        return Task.FromResult(new[] { hash, 1f, 0f });
    }
}

public sealed class FakeReranker : IReranker
{
    public Task<IReadOnlyList<ScoredNote>> RerankAsync(string query, IReadOnlyList<ScoredNote> candidates, CancellationToken ct = default) =>
        Task.FromResult(candidates);
}

public sealed class FakeSynthesizer(Func<string, IReadOnlyList<Note>, AnswerResult>? factory = null) : IAnswerSynthesizer
{
    public List<(string Query, IReadOnlyList<Note> Notes)> Calls { get; } = [];

    public Task<AnswerResult> SynthesizeAsync(string query, IReadOnlyList<Note> notes, CancellationToken ct = default)
    {
        Calls.Add((query, notes));
        return Task.FromResult(factory?.Invoke(query, notes) ?? new AnswerResult(true, "odpowiedz"));
    }
}

// Handler HTTP, ktory zapisuje zapytania i zwraca zaprogramowana odpowiedz.
public sealed class StubHttpHandler(Func<HttpRequestMessage, string, (HttpStatusCode Status, string Body)> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    public static StubHttpHandler Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new((_, _) => (status, body));

    // Odpowiedz w ksztalcie chat/completions z zadana trescia wiadomosci.
    public static StubHttpHandler Chat(string content) =>
        Json(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        var (status, text) = respond(request, body);
        return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    }

    public JsonDocument LastBody => JsonDocument.Parse(Requests[^1].Body);
}

public sealed class StubHttpClientFactory(StubHttpHandler handler) : IHttpClientFactory
{
    public string? LastName { get; private set; }

    public HttpClient CreateClient(string name)
    {
        LastName = name;
        return new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://localhost/") };
    }
}

public static class Sample
{
    public static Note Note(string title = "Tytul", string raw = "raw", string compressed = "compressed",
        string[]? tags = null, Guid? id = null, Guid? parentId = null, bool pinned = false, DateTimeOffset? created = null,
        string filePath = "") =>
        new(id ?? Guid.NewGuid(), title, raw, compressed, tags ?? ["a", "b"],
            created ?? new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
            created ?? new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
            filePath, parentId, pinned);

    public static JsonElement Args(object o) => JsonSerializer.SerializeToElement(o);
    public static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();
}

// Kompletny, prawdziwy stos rdzenia na plikach w katalogu tymczasowym z podmienialnymi LLM-ami.
public sealed class Stack : IDisposable
{
    public TempRoot Root { get; } = new();
    public ServiceProvider Services { get; }
    public FakeCompressor Compressor { get; } = new();
    public FakeEmbedder Embedder { get; } = new();
    public FakeSynthesizer Synthesizer { get; } = new();

    public INoteStore Store => Services.GetRequiredService<INoteStore>();
    public IVectorIndex Index => Services.GetRequiredService<IVectorIndex>();
    public NotePipeline Pipeline => Services.GetRequiredService<NotePipeline>();
    public NoteSearch Search => Services.GetRequiredService<NoteSearch>();
    public IEventBus Bus => Services.GetRequiredService<IEventBus>();

    public Stack(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Root.Notes);
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<INoteStore, FileNoteStore>();
        services.AddSingleton<IVectorIndex, FileVectorIndex>();
        services.AddSingleton<ICompressor>(Compressor);
        services.AddSingleton<IEmbedder>(Embedder);
        services.AddSingleton<IReranker, FakeReranker>();
        services.AddSingleton<IAnswerSynthesizer>(Synthesizer);
        services.AddSingleton<NoteSearch>();
        services.AddSingleton<NotePipeline>();
        configure?.Invoke(services);
        Services = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        Services.Dispose();
        Root.Dispose();
    }
}

// Handler zdarzen zapisujacy je do listy i opcjonalnie wykonujacy akcje (np. zmiana tagow).
public sealed class CaptureHandler<T>(Action<T>? action = null) : IEventHandler<T>
{
    public List<T> Seen { get; } = [];

    public Task HandleAsync(T e, CancellationToken ct = default)
    {
        Seen.Add(e);
        action?.Invoke(e);
        return Task.CompletedTask;
    }
}
