using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Core;
using SecondBrain.Infrastructure;
using SecondBrain.PipelineTest;
using SecondBrain.Plugins.Search;
using SecondBrain.Plugins.Sdk;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json")
    .Build();

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(config);
services.AddSecondBrainInfrastructure(config);
// Powloka no-op: zakladki pluginow bywaja tez handlerami zdarzen i wstrzykuja IShell.
services.AddSingleton<IShell, NullShell>();
services.AddSingleton<IEditorContext, NullEditorContext>();
// Backend pluginow tez w CLI - zeby `add` robil to samo co w Desktopie (patrz PLAN-PLUGINS.md 2.4).
PluginManager.Discover(typeof(SearchPlugin).Assembly).ConfigureServices(services, config);

await using var provider = services.BuildServiceProvider();
PluginRuntime.Services = provider;

var store = provider.GetRequiredService<IVectorIndex>();
var embedder = provider.GetRequiredService<IEmbedder>();
var answerSynthesizer = provider.GetRequiredService<IAnswerSynthesizer>();
var noteStore = provider.GetRequiredService<INoteStore>();
var pipeline = provider.GetRequiredService<NotePipeline>();
var noteSearch = provider.GetRequiredService<NoteSearch>();
var events = provider.GetRequiredService<IEventBus>();
var agent = provider.GetRequiredService<IAgent>();
var tagCleaner = provider.GetRequiredService<ITagCleaner>();
var tagMerger = provider.GetRequiredService<TagMerger>();
var ocrExtractor = provider.GetService<IOcrExtractor>();   // null gdy plugin OCR wylaczony
var duplicateScanner = provider.GetRequiredService<DuplicateScanner>();

async Task AddNoteAsync(string folder, string rawText)
{
    var added = await pipeline.AddAsync(folder, rawText);
    var note = added.Note;

    Console.WriteLine($"Zapisano: {note.FilePath}\nId: {note.Id}\nTytul (LLM): {note.Title}\nTagi (LLM): {string.Join(", ", note.Tags)}\nSkompresowano do: {note.CompressedContent}");

    if ((added.Result.Definitions ?? []).Length > 0)
        Console.WriteLine($"Definicje do slownika: {string.Join(", ", added.Result.Definitions!.Select(d => d.Term))}");

    foreach (var notice in added.Notices)
        Console.WriteLine(notice);
}

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
        var created = await pipeline.CreateFolderAsync(args[1]);
        Console.WriteLine(created ? $"Utworzono folder '{args[1]}'." : $"Folder '{args[1]}' juz istnieje.");
        break;
    }

    case "delete-folder" when args.Length >= 2:
    {
        var folder = args[1];
        await pipeline.DeleteFolderAsync(folder);
        Console.WriteLine($"Usunieto folder '{folder}' (indeks wektorowy + pliki na dysku, trwale).");
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
        await AddNoteAsync(folder, rawText);
        break;
    }

    case "ocr" when args.Length >= 3:
    {
        if (ocrExtractor is null)
        {
            Console.WriteLine("Plugin OCR wylaczony.");
            break;
        }

        var folder = args[1];
        var imagePath = args[2];
        var mimeType = Path.GetExtension(imagePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            var ext => throw new InvalidOperationException($"Nieobslugiwane rozszerzenie obrazka: {ext}")
        };

        var imageBytes = await File.ReadAllBytesAsync(imagePath);
        var extractedText = await ocrExtractor.ExtractTextAsync(imageBytes, mimeType);
        Console.WriteLine($"OCR odczytal:\n{extractedText}\n");

        await AddNoteAsync(folder, extractedText);
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

        await pipeline.TrashAsync(folder, note);
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
        var restored = await pipeline.RestoreAsync(trashPath);
        Console.WriteLine($"Przywrocono: {restored.Note.Title} -> folder '{restored.OriginalFolder}'.");
        break;
    }

    case "purge" when args.Length >= 2:
    {
        await pipeline.PurgeAsync(args[1]);
        Console.WriteLine("Usunieto na zawsze.");
        break;
    }

    case "search" when args.Length >= 3:
    {
        var folder = args[1];
        var query = string.Join(' ', args.Skip(2));

        var hits = await noteSearch.SearchAsync(query, [folder], vectorLimit: 20);

        var fullNotes = new List<Note>();

        Console.WriteLine($"Wyniki dla: \"{query}\"\n");
        foreach (var (_, note, score) in hits.Take(5))
        {
            Console.WriteLine($"[{score:0.00}] {note.Title} — tagi: {string.Join(", ", note.Tags)}");
            Console.WriteLine($"    {note.RawContent}");
            fullNotes.Add(note);
        }

        if (fullNotes.Count > 0)
        {
            var answer = await answerSynthesizer.SynthesizeAsync(query, fullNotes);
            Console.WriteLine($"\nOdpowiedz:\n{answer.Answer}");

            await events.PublishAsync(new SearchCompleted(query, answer.Answered));
            if (!answer.Answered)
                Console.WriteLine("(zapisano jako luka w wiedzy)");
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

    // Zloty plik do porownania przed/po refaktorze narzedzi agenta (PLAN-AGENT-PLUGINS.md P2).
    case "tools":
    {
        var registry = provider.GetRequiredService<SecondBrain.Infrastructure.AgentTools.AgentToolRegistry>();
        var all = (await registry.ListAsync()).OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        if (args.Length >= 2 && args[1] == "--mutating")
        {
            foreach (var t in all)
                Console.WriteLine($"{t.Name}\t{(t.IsMutating ? "mutating" : "readonly")}");
            break;
        }
        Console.WriteLine(FabrykaAgent.ToolDefinitions(all).ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        break;
    }

    case "agent":
    {
        Console.WriteLine("Czat z agentem. Pusta linia = wyjscie.\n");
        var state = "";

        while (true)
        {
            Console.Write("Ty: ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
                break;

            var step = await agent.SendAsync(state, input);
            state = step.ConversationState;

            while (step.PendingAction is { } pending)
            {
                Console.WriteLine($"Agent chce: {pending.Summary} [t/n]");
                var confirm = Console.ReadLine();
                step = await agent.ConfirmAsync(state, confirm?.Trim().Equals("t", StringComparison.OrdinalIgnoreCase) ?? false);
                state = step.ConversationState;
            }

            Console.WriteLine($"Agent: {step.ReplyText}\n");
        }
        break;
    }

    case "tags":
    {
        var allTags = new List<string>();
        foreach (var folder in await store.ListFoldersAsync())
            foreach (var note in await noteStore.ListAsync(folder))
                allTags.AddRange(note.Tags);

        var counts = allTags.GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (counts.Count == 0)
        {
            Console.WriteLine("Brak tagow.");
            break;
        }

        foreach (var g in counts)
            Console.WriteLine($"{g.Key} ({g.Count()})");

        var groups = await tagCleaner.FindDuplicateGroupsAsync(counts.Select(g => g.Key).ToList());
        if (groups.Count > 0)
        {
            Console.WriteLine("\nMozliwe duplikaty:");
            foreach (var group in groups)
                Console.WriteLine($"  [{string.Join(", ", group.Tags)}] -> {group.SuggestedCanonical}  (dotnet run -- merge-tags {group.SuggestedCanonical} {string.Join(' ', group.Tags.Where(t => !string.Equals(t, group.SuggestedCanonical, StringComparison.OrdinalIgnoreCase)))})");
        }
        break;
    }

    case "merge-tags" when args.Length >= 3:
    {
        var toTag = args[1];
        var fromTags = args.Skip(2).ToArray();

        var count = await tagMerger.MergeAsync(fromTags, toTag);
        Console.WriteLine($"Scalono {string.Join(", ", fromTags)} -> {toTag} w {count} notatce/-ach.");
        break;
    }

    case "glossary":
    {
        var entries = await noteStore.ListGlossaryAsync();
        if (entries.Count == 0)
        {
            Console.WriteLine("Slownik jest pusty.");
            break;
        }

        foreach (var e in entries)
            Console.WriteLine($"{e.Term} ({e.SourceTitle}): {e.Definition}");
        break;
    }

    case "fact-history" when args.Length >= 2:
    {
        var subject = string.Join(' ', args.Skip(1));
        var history = await noteStore.ListFactHistoryAsync(subject);
        if (history.Count == 0)
        {
            Console.WriteLine($"Brak historii dla '{subject}'.");
            break;
        }

        foreach (var v in history)
            Console.WriteLine($"{v.RecordedAt:yyyy-MM-dd HH:mm}  ({v.SourceTitle}): {v.Statement}");
        break;
    }

    case "find-duplicates":
    {
        var duplicates = await duplicateScanner.FindCrossFolderDuplicatesAsync();
        if (duplicates.Count == 0)
        {
            Console.WriteLine("Nie znaleziono duplikatow miedzyfolderowych.");
            break;
        }

        foreach (var (a, b, similarity) in duplicates)
            Console.WriteLine($"[{similarity:0.000}] \"{a.Title}\" <-> \"{b.Title}\"");
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
              dotnet run -- ocr <folder> <sciezka do obrazka .png/.jpg>
              dotnet run -- notes <folder>
              dotnet run -- delete-note <folder> <id notatki>   (do kosza)
              dotnet run -- list-trash
              dotnet run -- restore <sciezka z list-trash>
              dotnet run -- purge <sciezka z list-trash>        (trwale)
              dotnet run -- search <folder> <zapytanie...>
              dotnet run -- gaps
              dotnet run -- resolve-gap <sciezka z gaps>
              dotnet run -- glossary
              dotnet run -- agent
              dotnet run -- tags
              dotnet run -- merge-tags <docelowy-tag> <tag1> [tag2 ...]
              dotnet run -- fact-history <temat>
              dotnet run -- find-duplicates
            """);
        break;
}
