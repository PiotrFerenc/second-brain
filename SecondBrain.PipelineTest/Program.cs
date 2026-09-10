using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Core;
using SecondBrain.Infrastructure;
using SecondBrain.PipelineTest;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json")
    .Build();

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(config);
services.AddSecondBrainInfrastructure(config);

await using var provider = services.BuildServiceProvider();

var store = provider.GetRequiredService<IVectorIndex>();
var embedder = provider.GetRequiredService<IEmbedder>();
var reranker = provider.GetRequiredService<IReranker>();
var compressor = provider.GetRequiredService<ICompressor>();
var noteStore = provider.GetRequiredService<INoteStore>();

switch (args.ElementAtOrDefault(0))
{
    case "list":
    {
        var folders = await store.ListFoldersAsync();
        Console.WriteLine(folders.Count == 0 ? "Brak folderow." : string.Join("\n", folders));
        break;
    }

    case "create" when args.Length >= 2:
    {
        var created = await store.CreateFolderAsync(args[1]);
        Console.WriteLine(created ? $"Utworzono folder '{args[1]}'." : $"Folder '{args[1]}' juz istnieje.");
        break;
    }

    case "seed" when args.Length >= 2:
    {
        var folder = args[1];
        foreach (var note in SampleNotes.All)
        {
            var vector = await embedder.EmbedAsync(note.CompressedContent);
            await store.UpsertAsync(folder, note, vector);
            Console.WriteLine($"Zaindeksowano: {note.Title}");
        }
        break;
    }

    case "add" when args.Length >= 3:
    {
        var folder = args[1];
        var rawText = string.Join(' ', args.Skip(2));
        var now = DateTimeOffset.UtcNow;

        var result = await compressor.CompressAsync(rawText);
        var note = new Note(Guid.NewGuid(), result.Title, rawText, result.CompressedContent, [], now, now);

        var path = await noteStore.SaveAsync(folder, note);
        note = note with { FilePath = path };

        var vector = await embedder.EmbedAsync(note.CompressedContent);
        await store.UpsertAsync(folder, note, vector);

        Console.WriteLine($"Zapisano: {path}\nTytul (LLM): {result.Title}\nSkompresowano do: {result.CompressedContent}");
        break;
    }

    case "notes" when args.Length >= 2:
    {
        var notes = await noteStore.ListAsync(args[1]);
        if (notes.Count == 0)
        {
            Console.WriteLine("Ten folder jest jeszcze pusty.");
            break;
        }

        foreach (var note in notes)
            Console.WriteLine($"{note.UpdatedAt:yyyy-MM-dd HH:mm}  {note.Title}");
        break;
    }

    case "search" when args.Length >= 3:
    {
        var folder = args[1];
        var query = string.Join(' ', args.Skip(2));

        var queryVector = await embedder.EmbedAsync(query);
        var candidates = await store.SearchAsync(folder, queryVector, limit: 20);
        var reranked = await reranker.RerankAsync(query, candidates);

        Console.WriteLine($"Wyniki dla: \"{query}\"\n");
        foreach (var r in reranked.Take(5))
        {
            var note = r.Note;
            if (!string.IsNullOrEmpty(r.Note.FilePath) && File.Exists(r.Note.FilePath))
                note = await noteStore.LoadAsync(r.Note.FilePath);

            Console.WriteLine($"[{r.Score:0.00}] {note.Title} — tagi: {string.Join(", ", note.Tags)}");
            Console.WriteLine($"    {note.RawContent}");
        }
        break;
    }

    default:
        Console.WriteLine("""
            Uzycie:
              dotnet run -- list
              dotnet run -- create <folder>
              dotnet run -- seed <folder>
              dotnet run -- add <folder> <tekst notatki...>
              dotnet run -- notes <folder>
              dotnet run -- search <folder> <zapytanie...>
            """);
        break;
}
