using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

public class OpenAiAgent(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAiOptions> options,
    IVectorIndex vectorIndex,
    IEmbedder embedder,
    IReranker reranker,
    ICompressor compressor,
    IAnswerSynthesizer answerSynthesizer,
    IConflictDetector conflictDetector,
    INoteStore noteStore) : IAgent
{
    private readonly OpenAiOptions _options = options.Value;

    private const string SystemPrompt =
        "Jestes asystentem osobistej bazy wiedzy uzytkownika (Second Brain), dostepnym jako czat. " +
        "Masz dostep do narzedzi pozwalajacych przeszukiwac, czytac, dodawac i porzadkowac notatki " +
        "w folderach uzytkownika. Odpowiadaj po polsku, zwiezle i konkretnie. Gdy uzytkownik pyta o " +
        "cos co jest w notatkach, uzyj narzedzia zamiast zgadywac. Narzedzia, ktore cos zmieniaja, " +
        "same poprosza uzytkownika o potwierdzenie zanim sie wykonaja - po prostu je wywoluj, nie " +
        "pytaj o zgode w tresci wiadomosci.";

    // Narzedzia modyfikujace dane - wymagaja potwierdzenia usera przed wykonaniem.
    // Wszystko inne to czysty odczyt i wykonuje sie od razu.
    private static readonly HashSet<string> MutatingTools =
    [
        "create_folder", "add_note", "trash_note", "restore_note",
        "purge_note", "delete_folder", "resolve_gap"
    ];

    private static readonly JsonArray ToolDefinitions = (JsonArray)JsonNode.Parse("""
        [
          { "type": "function", "function": { "name": "list_folders", "description": "Wylistuj wszystkie foldery (kolekcje notatek) uzytkownika.", "parameters": { "type": "object", "properties": {} } } },
          { "type": "function", "function": { "name": "create_folder", "description": "Utworz nowy, pusty folder.", "parameters": { "type": "object", "properties": { "name": { "type": "string" } }, "required": ["name"] } } },
          { "type": "function", "function": { "name": "delete_folder", "description": "Usun caly folder wraz ze wszystkimi notatkami. Nieodwracalne.", "parameters": { "type": "object", "properties": { "folder": { "type": "string" } }, "required": ["folder"] } } },
          { "type": "function", "function": { "name": "list_notes", "description": "Wylistuj notatki (id, tytul, tagi) w danym folderze.", "parameters": { "type": "object", "properties": { "folder": { "type": "string" } }, "required": ["folder"] } } },
          { "type": "function", "function": { "name": "get_note", "description": "Pobierz pelna tresc jednej notatki po id.", "parameters": { "type": "object", "properties": { "folder": { "type": "string" }, "noteId": { "type": "string" } }, "required": ["folder", "noteId"] } } },
          { "type": "function", "function": { "name": "search_notes", "description": "Semantyczne wyszukiwanie notatek pasujacych do zapytania. Bez folderu przeszukuje wszystkie foldery.", "parameters": { "type": "object", "properties": { "query": { "type": "string" }, "folder": { "type": "string" } }, "required": ["query"] } } },
          { "type": "function", "function": { "name": "ask_question", "description": "Zadaj pytanie do bazy notatek (RAG) - zwraca zsyntetyzowana odpowiedz na podstawie znalezionych notatek. Bez folderu przeszukuje wszystkie foldery.", "parameters": { "type": "object", "properties": { "query": { "type": "string" }, "folder": { "type": "string" } }, "required": ["query"] } } },
          { "type": "function", "function": { "name": "add_note", "description": "Dodaj nowa notatke do folderu - tekst zostanie skompresowany i zindeksowany przez LLM.", "parameters": { "type": "object", "properties": { "folder": { "type": "string" }, "text": { "type": "string" } }, "required": ["folder", "text"] } } },
          { "type": "function", "function": { "name": "trash_note", "description": "Przenies notatke do kosza.", "parameters": { "type": "object", "properties": { "folder": { "type": "string" }, "noteId": { "type": "string" } }, "required": ["folder", "noteId"] } } },
          { "type": "function", "function": { "name": "list_trash", "description": "Wylistuj notatki w koszu.", "parameters": { "type": "object", "properties": {} } } },
          { "type": "function", "function": { "name": "restore_note", "description": "Przywroc notatke z kosza do jej pierwotnego folderu.", "parameters": { "type": "object", "properties": { "trashPath": { "type": "string" } }, "required": ["trashPath"] } } },
          { "type": "function", "function": { "name": "purge_note", "description": "Trwale usun notatke z kosza. Nieodwracalne.", "parameters": { "type": "object", "properties": { "trashPath": { "type": "string" } }, "required": ["trashPath"] } } },
          { "type": "function", "function": { "name": "list_gaps", "description": "Wylistuj luki w wiedzy - pytania, na ktore baza notatek nie miala jeszcze odpowiedzi.", "parameters": { "type": "object", "properties": {} } } },
          { "type": "function", "function": { "name": "resolve_gap", "description": "Odrzuc / zamknij luke w wiedzy bez odpowiadania na nia.", "parameters": { "type": "object", "properties": { "path": { "type": "string" } }, "required": ["path"] } } },
          { "type": "function", "function": { "name": "list_glossary", "description": "Wylistuj slownik pojec zbudowany automatycznie z notatek.", "parameters": { "type": "object", "properties": {} } } }
        ]
        """)!;

    public async Task<AgentStepResult> SendAsync(string conversationState, string userMessage, CancellationToken ct = default)
    {
        var messages = LoadMessages(conversationState);
        messages.Add(new JsonObject { ["role"] = "user", ["content"] = userMessage });
        return await RunLoopAsync(messages, ct);
    }

    public async Task<AgentStepResult> ConfirmAsync(string conversationState, bool approved, CancellationToken ct = default)
    {
        var messages = LoadMessages(conversationState);
        var pendingMessage = messages[^1]!.AsObject();
        var call = ((JsonArray)pendingMessage["tool_calls"]!)[0]!.AsObject();
        var function = call["function"]!.AsObject();

        var resultContent = approved
            ? await ExecuteToolAsync(function["name"]!.GetValue<string>(), function["arguments"]!.GetValue<string>(), ct)
            : "Uzytkownik odrzucil te akcje, nie zostala wykonana.";

        messages.Add(new JsonObject
        {
            ["role"] = "tool",
            ["tool_call_id"] = call["id"]!.GetValue<string>(),
            ["content"] = resultContent
        });

        return await RunLoopAsync(messages, ct);
    }

    // ponytail: twardy limit hopow narzedzi na jedna wiadomosc - zabezpieczenie przed
    // modelem petlacym sie w kolko, nie realny scenariusz przy dzialajacych narzedziach.
    private const int MaxToolHops = 8;

    private async Task<AgentStepResult> RunLoopAsync(JsonArray messages, CancellationToken ct)
    {
        var executedLog = new List<string>();

        for (var hop = 0; hop < MaxToolHops; hop++)
        {
            var assistantMessage = await CallChatCompletionsAsync(messages, ct);
            messages.Add(assistantMessage);

            var toolCalls = assistantMessage["tool_calls"] as JsonArray;
            if (toolCalls is null || toolCalls.Count == 0)
            {
                var text = assistantMessage["content"]?.GetValue<string>() ?? "";
                return new AgentStepResult(Serialize(messages), text, null, executedLog);
            }

            var call = toolCalls[0]!.AsObject();
            var function = call["function"]!.AsObject();
            var toolName = function["name"]!.GetValue<string>();
            var argsJson = function["arguments"]!.GetValue<string>();

            if (MutatingTools.Contains(toolName))
                return new AgentStepResult(Serialize(messages), null, new AgentPendingAction(toolName, argsJson, DescribeAction(toolName, argsJson)), executedLog);

            var result = await ExecuteToolAsync(toolName, argsJson, ct);
            executedLog.Add($"{toolName}({argsJson})");
            messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = call["id"]!.GetValue<string>(), ["content"] = result });
        }

        return new AgentStepResult(Serialize(messages), "Zbyt wiele krokow narzedzi w jednej turze - przerywam.", null, executedLog);
    }

    private async Task<JsonObject> CallChatCompletionsAsync(JsonArray conversation, CancellationToken ct)
    {
        var allMessages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = SystemPrompt } };
        foreach (var m in conversation)
            allMessages.Add(m!.DeepClone());

        var client = httpClientFactory.CreateClient("OpenAI");
        var requestBody = new JsonObject
        {
            ["model"] = _options.AgentModel,
            ["messages"] = allMessages,
            ["tools"] = ToolDefinitions.DeepClone(),
            ["tool_choice"] = "auto",
            ["parallel_tool_calls"] = false
        };

        var response = await client.PostAsJsonAsync("chat/completions", requestBody, ct);
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonNode.Parse(raw) ?? throw new InvalidOperationException("Pusta odpowiedz OpenAI chat/completions.");
        var message = doc["choices"]![0]!["message"]!.AsObject();
        return (JsonObject)message.DeepClone();
    }

    private static JsonArray LoadMessages(string conversationState) =>
        string.IsNullOrWhiteSpace(conversationState) ? [] : JsonNode.Parse(conversationState)!.AsArray();

    private static string Serialize(JsonArray messages) => messages.ToJsonString();

    private static string DescribeAction(string toolName, string argsJson)
    {
        var args = JsonDocument.Parse(argsJson).RootElement;
        string S(string name) => args.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";

        return toolName switch
        {
            "create_folder" => $"Utworzyc nowy folder '{S("name")}'?",
            "add_note" => $"Dodac notatke do folderu '{S("folder")}': \"{Truncate(S("text"), 100)}\"?",
            "trash_note" => $"Przeniesc notatke {S("noteId")} z folderu '{S("folder")}' do kosza?",
            "restore_note" => $"Przywrocic notatke z kosza ({S("trashPath")})?",
            "purge_note" => $"TRWALE usunac notatke z kosza ({S("trashPath")})? Tej operacji nie mozna cofnac.",
            "delete_folder" => $"TRWALE usunac caly folder '{S("folder")}' wraz z notatkami? Tej operacji nie mozna cofnac.",
            "resolve_gap" => $"Odrzucic luke w wiedzy ({S("path")})?",
            _ => $"Wykonac akcje '{toolName}' z argumentami {argsJson}?"
        };
    }

    private async Task<string> ExecuteToolAsync(string toolName, string argsJson, CancellationToken ct)
    {
        var args = JsonDocument.Parse(argsJson).RootElement;
        string Req(string name) => args.GetProperty(name).GetString()!;
        string? Opt(string name) => args.TryGetProperty(name, out var v) ? v.GetString() : null;

        switch (toolName)
        {
            case "list_folders":
                return JsonSerializer.Serialize(await vectorIndex.ListFoldersAsync(ct));

            case "create_folder":
            {
                var name = Req("name");
                var created = await vectorIndex.CreateFolderAsync(name, ct);
                return created ? $"Utworzono folder '{name}'." : $"Folder '{name}' juz istnieje.";
            }

            case "delete_folder":
            {
                var folder = Req("folder");
                await vectorIndex.DeleteFolderAsync(folder, ct);
                await noteStore.DeleteFolderAsync(folder, ct);
                return $"Usunieto folder '{folder}' trwale.";
            }

            case "list_notes":
            {
                var notes = await noteStore.ListAsync(Req("folder"), ct);
                return JsonSerializer.Serialize(notes.Select(n => new { n.Id, n.Title, n.Tags, n.Pinned, n.UpdatedAt }));
            }

            case "get_note":
            {
                var notes = await noteStore.ListAsync(Req("folder"), ct);
                var noteId = Req("noteId");
                var note = notes.FirstOrDefault(n => n.Id.ToString() == noteId);
                return note is null
                    ? $"Nie znaleziono notatki {noteId}."
                    : JsonSerializer.Serialize(new { note.Id, note.Title, note.CompressedContent, note.Tags, note.Pinned });
            }

            case "search_notes":
            {
                var matches = await SearchAcrossAsync(Req("query"), Opt("folder"), ct);
                return JsonSerializer.Serialize(matches.Take(10).Select(m => new
                {
                    m.Folder,
                    m.Scored.Note.Id,
                    m.Scored.Note.Title,
                    m.Scored.Note.Tags,
                    m.Scored.Score,
                    Snippet = Truncate(m.Scored.Note.CompressedContent, 200)
                }));
            }

            case "ask_question":
            {
                var query = Req("query");
                var matches = await SearchAcrossAsync(query, Opt("folder"), ct);
                var fullNotes = new List<Note>();
                foreach (var m in matches.Take(5))
                {
                    var note = m.Scored.Note;
                    if (!string.IsNullOrEmpty(note.FilePath) && File.Exists(note.FilePath))
                        note = await noteStore.LoadAsync(note.FilePath, ct);
                    fullNotes.Add(note);
                }

                if (fullNotes.Count == 0)
                    return "Brak notatek pasujacych do tego pytania.";

                var answer = await answerSynthesizer.SynthesizeAsync(query, fullNotes, ct);
                if (!answer.Answered)
                    await noteStore.LogGapAsync(query, ct);
                return answer.Answer;
            }

            case "add_note":
            {
                var folder = Req("folder");
                var text = Req("text");
                var now = DateTimeOffset.UtcNow;

                var result = await compressor.CompressAsync(text, ct);
                var note = new Note(Guid.NewGuid(), result.Title, text, result.CompressedContent, result.Tags, now, now);
                var path = await noteStore.SaveAsync(folder, note, ct);
                note = note with { FilePath = path };

                foreach (var def in result.Definitions ?? [])
                    await noteStore.SaveGlossaryEntryAsync(def.Term, def.Definition, result.Title, ct);

                var vector = await embedder.EmbedAsync(note.CompressedContent, ct);
                await vectorIndex.UpsertAsync(folder, note, vector, ct);

                var related = await vectorIndex.SearchAsync(folder, vector, limit: 4, ct: ct);
                var relatedNotes = related.Where(r => r.Note.Id != note.Id).Take(3).Select(r => r.Note).ToList();

                var reply = $"Zapisano notatke '{result.Title}' w folderze '{folder}'.";
                if (relatedNotes.Count > 0)
                {
                    var conflict = await conflictDetector.DetectAsync(note.CompressedContent, relatedNotes, ct);
                    if (conflict.HasConflict)
                        reply += $" UWAGA - mozliwa sprzecznosc z \"{conflict.ConflictingTitle}\": {conflict.Explanation}";
                }
                return reply;
            }

            case "trash_note":
            {
                var folder = Req("folder");
                var noteId = Guid.Parse(Req("noteId"));
                var notes = await noteStore.ListAsync(folder, ct);
                var note = notes.FirstOrDefault(n => n.Id == noteId);
                if (note is null)
                    return $"Nie znaleziono notatki {noteId} w folderze '{folder}'.";

                await vectorIndex.DeleteNoteAsync(folder, noteId, ct);
                await noteStore.MoveToTrashAsync(folder, note.FilePath, ct);
                return $"Przeniesiono do kosza: {note.Title}";
            }

            case "list_trash":
            {
                var trashed = await noteStore.ListTrashAsync(ct);
                return JsonSerializer.Serialize(trashed.Select(t => new { t.TrashPath, t.OriginalFolder, t.Note.Title }));
            }

            case "restore_note":
            {
                var restored = await noteStore.RestoreFromTrashAsync(Req("trashPath"), ct);
                var vector = await embedder.EmbedAsync(restored.Note.CompressedContent, ct);
                await vectorIndex.UpsertAsync(restored.OriginalFolder, restored.Note, vector, ct);
                return $"Przywrocono '{restored.Note.Title}' do folderu '{restored.OriginalFolder}'.";
            }

            case "purge_note":
                await noteStore.PurgeTrashAsync(Req("trashPath"), ct);
                return "Usunieto trwale.";

            case "list_gaps":
                return JsonSerializer.Serialize(await noteStore.ListGapsAsync(ct));

            case "resolve_gap":
                await noteStore.ResolveGapAsync(Req("path"), ct);
                return "Odrzucono luke.";

            case "list_glossary":
                return JsonSerializer.Serialize(await noteStore.ListGlossaryAsync(ct));

            default:
                return $"Nieznane narzedzie: {toolName}";
        }
    }

    private async Task<List<(string Folder, ScoredNote Scored)>> SearchAcrossAsync(string query, string? folder, CancellationToken ct)
    {
        var queryVector = await embedder.EmbedAsync(query, ct);
        IReadOnlyList<string> folders = folder is not null ? [folder] : await vectorIndex.ListFoldersAsync(ct);

        var candidates = new List<(string Folder, ScoredNote Scored)>();
        foreach (var f in folders)
        {
            var results = await vectorIndex.SearchAsync(f, queryVector, limit: 10, ct: ct);
            candidates.AddRange(results.Select(r => (f, r)));
        }

        var folderById = candidates.ToDictionary(c => c.Scored.Note.Id, c => c.Folder);
        var reranked = await reranker.RerankAsync(query, candidates.Select(c => c.Scored).ToList(), ct);
        return reranked.Select(r => (folderById.GetValueOrDefault(r.Note.Id, folder ?? ""), r)).ToList();
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "...";
}
