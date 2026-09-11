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
var answerSynthesizer = provider.GetRequiredService<IAnswerSynthesizer>();
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

    case "delete-folder" when args.Length >= 2:
    {
        var folder = args[1];
        await store.DeleteFolderAsync(folder);
        await noteStore.DeleteFolderAsync(folder);
        Console.WriteLine($"Usunieto folder '{folder}' (kolekcja Qdrant + pliki na dysku, trwale).");
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
        var note = new Note(Guid.NewGuid(), result.Title, rawText, result.CompressedContent, result.Tags, now, now);

        var path = await noteStore.SaveAsync(folder, note);
        note = note with { FilePath = path };

        var vector = await embedder.EmbedAsync(note.CompressedContent);
        await store.UpsertAsync(folder, note, vector);

        Console.WriteLine($"Zapisano: {path}\nId: {note.Id}\nTytul (LLM): {result.Title}\nTagi (LLM): {string.Join(", ", result.Tags)}\nSkompresowano do: {result.CompressedContent}");
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
            Console.WriteLine($"{note.Id}  {note.UpdatedAt:yyyy-MM-dd HH:mm}  {(note.Pinned ? "[przypieta] " : "")}{note.Title}");
        break;
    }

    case "delete-note" when args.Length >= 3:
    {
        var folder = args[1];
        var noteId = Guid.Parse(args[2]);

        var notes = await noteStore.ListAsync(folder);
        var note = notes.FirstOrDefault(n => n.Id == noteId);
        if (note is null)
        {
            Console.WriteLine($"Nie znaleziono notatki {noteId} w folderze '{folder}'.");
            break;
        }

        await store.DeleteNoteAsync(folder, noteId);
        await noteStore.MoveToTrashAsync(folder, note.FilePath);
        Console.WriteLine($"Przeniesiono do kosza: {note.Title}");
        break;
    }

    case "list-trash":
    {
        var trashed = await noteStore.ListTrashAsync();
        if (trashed.Count == 0)
        {
            Console.WriteLine("Kosz jest pusty.");
            break;
        }

        foreach (var t in trashed)
            Console.WriteLine($"{t.TrashPath}  ({t.OriginalFolder})  {t.Note.Title}");
        break;
    }

    case "restore" when args.Length >= 2:
    {
        var trashPath = args[1];
        var restored = await noteStore.RestoreFromTrashAsync(trashPath);
        var vector = await embedder.EmbedAsync(restored.Note.CompressedContent);
        await store.UpsertAsync(restored.OriginalFolder, restored.Note, vector);
        Console.WriteLine($"Przywrocono: {restored.Note.Title} -> folder '{restored.OriginalFolder}'.");
        break;
    }

    case "purge" when args.Length >= 2:
    {
        await noteStore.PurgeTrashAsync(args[1]);
        Console.WriteLine("Usunieto na zawsze.");
        break;
    }

    case "search" when args.Length >= 3:
    {
        var folder = args[1];
        var query = string.Join(' ', args.Skip(2));

        var queryVector = await embedder.EmbedAsync(query);
        var candidates = await store.SearchAsync(folder, queryVector, limit: 20);
        var reranked = await reranker.RerankAsync(query, candidates);

        var fullNotes = new List<Note>();

        Console.WriteLine($"Wyniki dla: \"{query}\"\n");
        foreach (var r in reranked.Take(5))
        {
            var note = r.Note;
            if (!string.IsNullOrEmpty(r.Note.FilePath) && File.Exists(r.Note.FilePath))
                note = await noteStore.LoadAsync(r.Note.FilePath);

            Console.WriteLine($"[{r.Score:0.00}] {note.Title} — tagi: {string.Join(", ", note.Tags)}");
            Console.WriteLine($"    {note.RawContent}");
            fullNotes.Add(note);
        }

        if (fullNotes.Count > 0)
        {
            var answer = await answerSynthesizer.SynthesizeAsync(query, fullNotes);
            Console.WriteLine($"\nOdpowiedz:\n{answer.Answer}");

            if (!answer.Answered)
            {
                await noteStore.LogGapAsync(query);
                Console.WriteLine("(zapisano jako luka w wiedzy)");
            }
        }
        break;
    }

    case "gaps":
    {
        var gaps = await noteStore.ListGapsAsync();
        if (gaps.Count == 0)
        {
            Console.WriteLine("Brak luk w wiedzy.");
            break;
        }

        foreach (var g in gaps)
            Console.WriteLine($"{g.Path}  {g.AskedAt:yyyy-MM-dd HH:mm}  {g.Query}");
        break;
    }

    case "resolve-gap" when args.Length >= 2:
    {
        await noteStore.ResolveGapAsync(args[1]);
        Console.WriteLine("Odrzucono luke.");
        break;
    }

    default:
        Console.WriteLine("""
            Uzycie:
              dotnet run -- list
              dotnet run -- create <folder>
              dotnet run -- delete-folder <folder>
              dotnet run -- seed <folder>
              dotnet run -- add <folder> <tekst notatki...>
              dotnet run -- notes <folder>
              dotnet run -- delete-note <folder> <id notatki>   (do kosza)
              dotnet run -- list-trash
              dotnet run -- restore <sciezka z list-trash>
              dotnet run -- purge <sciezka z list-trash>        (trwale)
              dotnet run -- search <folder> <zapytanie...>
              dotnet run -- gaps
              dotnet run -- resolve-gap <sciezka z gaps>
            """);
        break;
}
