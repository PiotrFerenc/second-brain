using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

public class FabrykaAgent(
    IHttpClientFactory httpClientFactory,
    IOptions<AgentOptions> options,
    IVectorIndex vectorIndex,
    IEmbedder embedder,
    IReranker reranker,
    ICompressor compressor,
    IAnswerSynthesizer answerSynthesizer,
    IConflictDetector conflictDetector,
    INoteStore noteStore,
    GapAutoCloser gapAutoCloser,
    ITagCleaner tagCleaner,
    TagMerger tagMerger,
    DuplicateScanner duplicateScanner) : IAgent
{
    private readonly AgentOptions _options = options.Value;

    // Narzedzia modyfikujace dane - wymagaja potwierdzenia usera przed wykonaniem.
    // Wszystko inne to czysty odczyt i wykonuje sie od razu.
    private static readonly HashSet<string> MutatingTools =
    [
        "create_folder", "add_note", "trash_note", "restore_note",
        "purge_note", "delete_folder", "resolve_gap", "move_note",
        "edit_note", "set_note_pinned", "merge_tags", "bulk_import",
        "bulk_trash", "bulk_move", "bulk_tag", "bulk_pin", "purge_trash_all",
        "bulk_create_folders", "bulk_delete_folders", "bulk_restore",
        "bulk_purge", "bulk_resolve_gaps", "add_glossary_entry", "delete_glossary_entry",
        "bulk_note_create", "bulk_note_update"
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
          { "type": "function", "function": { "name": "add_note", "description": "Dodaj nowa notatke do folderu - tekst zostanie skompresowany i zindeksowany przez LLM.", "parameters": { "type": "object", "properties": { "folder": { "type": "string" }, "text": { "type": "string" }, "parentId": { "type": "string", "description": "Opcjonalne id notatki-rodzica w tym samym folderze, zeby od razu zagniezdzic nowa notatke." } }, "required": ["folder", "text"] } } },
          { "type": "function", "function": { "name": "edit_note", "description": "Edytuj tresc istniejacej notatki - nowy tekst zostanie skompresowany na nowo (tytul/tagi tez sie przelicza) i podmieni stara tresc.", "parameters": { "type": "object", "properties": { "folder": { "type": "string" }, "noteId": { "type": "string" }, "text": { "type": "string" } }, "required": ["folder", "noteId", "text"] } } },
          { "type": "function", "function": { "name": "set_note_pinned", "description": "Przypnij lub odepnij notatke.", "parameters": { "type": "object", "properties": { "folder": { "type": "string" }, "noteId": { "type": "string" }, "pinned": { "type": "boolean" } }, "required": ["folder", "noteId", "pinned"] } } },
          { "type": "function", "function": { "name": "bulk_import", "description": "Zaimportuj wiele notatek naraz - kazda linia z listy staje sie osobna notatka (kompresja+indeksowanie), bez wykrywania sprzecznosci (za duzo wywolan LLM przy imporcie).", "parameters": { "type": "object", "properties": { "folder": { "type": "string" }, "lines": { "type": "array", "items": { "type": "string" } } }, "required": ["folder", "lines"] } } },
          { "type": "function", "function": { "name": "list_templates", "description": "Wylistuj szablony notatek (nazwa + tresc) dostepne do wykorzystania przed dodaniem notatki.", "parameters": { "type": "object", "properties": {} } } },
          { "type": "function", "function": { "name": "fact_history", "description": "Pokaz historie sprzecznych wersji faktu dla danego tematu (wykryte wczesniej przez wykrywacz sprzecznosci).", "parameters": { "type": "object", "properties": { "subject": { "type": "string" } }, "required": ["subject"] } } },
          { "type": "function", "function": { "name": "list_by_tag", "description": "Wylistuj notatki majace dokladnie podany tag (nie semantyczne - dokladne dopasowanie tagu). Bez folderu przeszukuje wszystkie foldery.", "parameters": { "type": "object", "properties": { "tag": { "type": "string" }, "folder": { "type": "string" } }, "required": ["tag"] } } },
          { "type": "function", "function": { "name": "get_note_tree", "description": "Pokaz zagniezdzona strukture notatek (rodzic/dzieci) w danym folderze - przydatne przed uzyciem move_note/add_note z parentId.", "parameters": { "type": "object", "properties": { "folder": { "type": "string" } }, "required": ["folder"] } } },
          { "type": "function", "function": { "name": "find_duplicate_tags", "description": "Znajdz grupy potencjalnie zduplikowanych tagow (liczba pojedyncza/mnoga, literowki, synonimy) w calej bazie - kandydaci do merge_tags.", "parameters": { "type": "object", "properties": {} } } },
          { "type": "function", "function": { "name": "merge_tags", "description": "Scal liste tagow w jeden kanoniczny tag we wszystkich notatkach (wszystkie foldery).", "parameters": { "type": "object", "properties": { "fromTags": { "type": "array", "items": { "type": "string" } }, "toTag": { "type": "string" } }, "required": ["fromTags", "toTag"] } } },
          { "type": "function", "function": { "name": "find_duplicate_notes", "description": "Znajdz notatki niemal identyczne (semantycznie) zyjace w dwoch roznych folderach - kandydaci do recznego scalenia.", "parameters": { "type": "object", "properties": { "threshold": { "type": "number", "description": "Prog podobienstwa 0-1, domyslnie 0.92." } } } } },
          { "type": "function", "function": { "name": "trash_note", "description": "Przenies notatke do kosza.", "parameters": { "type": "object", "properties": { "folder": { "type": "string" }, "noteId": { "type": "string" } }, "required": ["folder", "noteId"] } } },
          { "type": "function", "function": { "name": "list_trash", "description": "Wylistuj notatki w koszu.", "parameters": { "type": "object", "properties": {} } } },
          { "type": "function", "function": { "name": "restore_note", "description": "Przywroc notatke z kosza do jej pierwotnego folderu.", "parameters": { "type": "object", "properties": { "trashPath": { "type": "string" } }, "required": ["trashPath"] } } },
          { "type": "function", "function": { "name": "purge_note", "description": "Trwale usun notatke z kosza. Nieodwracalne.", "parameters": { "type": "object", "properties": { "trashPath": { "type": "string" } }, "required": ["trashPath"] } } },
          { "type": "function", "function": { "name": "list_gaps", "description": "Wylistuj luki w wiedzy - pytania, na ktore baza notatek nie miala jeszcze odpowiedzi.", "parameters": { "type": "object", "properties": {} } } },
          { "type": "function", "function": { "name": "resolve_gap", "description": "Odrzuc / zamknij luke w wiedzy bez odpowiadania na nia.", "parameters": { "type": "object", "properties": { "path": { "type": "string" } }, "required": ["path"] } } },
          { "type": "function", "function": { "name": "list_glossary", "description": "Wylistuj slownik pojec zbudowany automatycznie z notatek.", "parameters": { "type": "object", "properties": {} } } },
          { "type": "function", "function": { "name": "move_note", "description": "Przenies notatke do innego folderu i/lub zagniezdz ja pod inna notatka. Podaj przynajmniej jedno z: targetFolder, newParentId. newParentId='root' odpina notatke na najwyzszy poziom folderu. Notatki z wlasnymi podnotatkami nie mozna przeniesc miedzy folderami.", "parameters": { "type": "object", "properties": { "folder": { "type": "string", "description": "Aktualny folder notatki." }, "noteId": { "type": "string" }, "targetFolder": { "type": "string", "description": "Nowy folder notatki." }, "newParentId": { "type": "string", "description": "Id notatki-rodzica (w folderze docelowym) albo 'root'." } }, "required": ["folder", "noteId"] } } },
          { "type": "function", "function": { "name": "bulk_trash", "description": "Przenies wiele notatek naraz do kosza (po liscie id, wszystkie w jednym folderze).", "parameters": { "type": "object", "properties": { "folder": { "type": "string" }, "noteIds": { "type": "array", "items": { "type": "string" } } }, "required": ["folder", "noteIds"] } } },
          { "type": "function", "function": { "name": "bulk_move", "description": "Przenies wiele notatek naraz do innego folderu i/lub pod innego rodzica. Podaj przynajmniej jedno z: targetFolder, newParentId.", "parameters": { "type": "object", "properties": { "folder": { "type": "string" }, "noteIds": { "type": "array", "items": { "type": "string" } }, "targetFolder": { "type": "string" }, "newParentId": { "type": "string" } }, "required": ["folder", "noteIds"] } } },
          { "type": "function", "function": { "name": "bulk_tag", "description": "Dodaj i/lub usun tagi na wielu notatkach naraz.", "parameters": { "type": "object", "properties": { "folder": { "type": "string" }, "noteIds": { "type": "array", "items": { "type": "string" } }, "addTags": { "type": "array", "items": { "type": "string" } }, "removeTags": { "type": "array", "items": { "type": "string" } } }, "required": ["folder", "noteIds"] } } },
          { "type": "function", "function": { "name": "bulk_pin", "description": "Przypnij lub odepnij wiele notatek naraz.", "parameters": { "type": "object", "properties": { "folder": { "type": "string" }, "noteIds": { "type": "array", "items": { "type": "string" } }, "pinned": { "type": "boolean" } }, "required": ["folder", "noteIds", "pinned"] } } },
          { "type": "function", "function": { "name": "purge_trash_all", "description": "Trwale usun WSZYSTKIE notatki z kosza naraz. Nieodwracalne.", "parameters": { "type": "object", "properties": {} } } },
          { "type": "function", "function": { "name": "export_folder", "description": "Wyeksportuj caly folder jako jeden tekst markdown (wszystkie notatki polaczone) - do skopiowania/backupu.", "parameters": { "type": "object", "properties": { "folder": { "type": "string" } }, "required": ["folder"] } } },
          { "type": "function", "function": { "name": "bulk_create_folders", "description": "Utworz wiele nowych, pustych folderow naraz.", "parameters": { "type": "object", "properties": { "names": { "type": "array", "items": { "type": "string" } } }, "required": ["names"] } } },
          { "type": "function", "function": { "name": "bulk_delete_folders", "description": "Usun wiele folderow naraz wraz z ich notatkami. Nieodwracalne.", "parameters": { "type": "object", "properties": { "folders": { "type": "array", "items": { "type": "string" } } }, "required": ["folders"] } } },
          { "type": "function", "function": { "name": "bulk_restore", "description": "Przywroc wiele notatek z kosza naraz do ich pierwotnych folderow.", "parameters": { "type": "object", "properties": { "trashPaths": { "type": "array", "items": { "type": "string" } } }, "required": ["trashPaths"] } } },
          { "type": "function", "function": { "name": "bulk_purge", "description": "Trwale usun z kosza wybrana liste notatek naraz (w przeciwienstwie do purge_trash_all, ktore czysci caly kosz). Nieodwracalne.", "parameters": { "type": "object", "properties": { "trashPaths": { "type": "array", "items": { "type": "string" } } }, "required": ["trashPaths"] } } },
          { "type": "function", "function": { "name": "bulk_resolve_gaps", "description": "Odrzuc/zamknij wiele luk w wiedzy naraz bez odpowiadania na nie.", "parameters": { "type": "object", "properties": { "paths": { "type": "array", "items": { "type": "string" } } }, "required": ["paths"] } } },
          { "type": "function", "function": { "name": "add_glossary_entry", "description": "Dodaj recznie wpis do slownika pojec (albo nadpisz istniejacy o tym samym terminie).", "parameters": { "type": "object", "properties": { "term": { "type": "string" }, "definition": { "type": "string" } }, "required": ["term", "definition"] } } },
          { "type": "function", "function": { "name": "delete_glossary_entry", "description": "Usun wpis ze slownika pojec po terminie.", "parameters": { "type": "object", "properties": { "term": { "type": "string" } }, "required": ["term"] } } },
          { "type": "function", "function": { "name": "bulk_note_create", "description": "Dodaj wiele notatek naraz do jednego folderu - notatki podane jako jeden string, oddzielone przecinkami (kazda staje sie osobna notatka, kompresja+indeksowanie). Prostsza alternatywa dla bulk_import gdy user podaje notatki po przecinku w tekscie.", "parameters": { "type": "object", "properties": { "folder": { "type": "string" }, "notes": { "type": "string", "description": "Notatki oddzielone przecinkami, np. 'Kup mleko, Zadzwon do Jana, Zaplac czynsz'." } }, "required": ["folder", "notes"] } } },
          { "type": "function", "function": { "name": "bulk_note_update", "description": "Zaktualizuj wiele notatek naraz w jednym folderze - wpisy podane jako jeden string, oddzielone przecinkami, kazdy w formacie 'noteId: nowa tresc'. Ustal id notatek najpierw przez list_notes/search_notes.", "parameters": { "type": "object", "properties": { "folder": { "type": "string" }, "updates": { "type": "string", "description": "Wpisy oddzielone przecinkami, np. '3fa8...: Nowa tresc, 9c12...: Inna tresc'." } }, "required": ["folder", "updates"] } } }
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
        var allMessages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = _options.SystemPrompt } };
        foreach (var m in conversation)
            allMessages.Add(m!.DeepClone());

        var client = httpClientFactory.CreateClient("Agent");
        var requestBody = new JsonObject
        {
            ["model"] = _options.Model,
            ["messages"] = allMessages,
            ["tools"] = ToolDefinitions.DeepClone(),
            ["tool_choice"] = "auto",
            ["parallel_tool_calls"] = false
        };

        var response = await client.PostAsJsonAsync("chat/completions", requestBody, ct);
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonNode.Parse(raw) ?? throw new InvalidOperationException("Pusta odpowiedz Fabryka chat/completions.");
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
        string[] SArr(string name) => args.TryGetProperty(name, out var v) ? v.EnumerateArray().Select(e => e.GetString() ?? "").ToArray() : [];

        return toolName switch
        {
            "create_folder" => $"Utworzyc nowy folder '{S("name")}'?",
            "add_note" => $"Dodac notatke do folderu '{S("folder")}': \"{Truncate(S("text"), 100)}\"?",
            "edit_note" => $"Zedytowac notatke {S("noteId")} w folderze '{S("folder")}' - nowa tresc: \"{Truncate(S("text"), 100)}\"?",
            "set_note_pinned" => $"{(args.GetProperty("pinned").GetBoolean() ? "Przypiac" : "Odpiac")} notatke {S("noteId")}?",
            "bulk_import" => $"Zaimportowac {args.GetProperty("lines").EnumerateArray().Count(e => !string.IsNullOrWhiteSpace(e.GetString()))} notatek do folderu '{S("folder")}'?",
            "merge_tags" => $"Scalic tagi [{string.Join(", ", SArr("fromTags"))}] w tag '{S("toTag")}' we wszystkich notatkach?",
            "trash_note" => $"Przeniesc notatke {S("noteId")} z folderu '{S("folder")}' do kosza?",
            "restore_note" => $"Przywrocic notatke z kosza ({S("trashPath")})?",
            "purge_note" => $"TRWALE usunac notatke z kosza ({S("trashPath")})? Tej operacji nie mozna cofnac.",
            "delete_folder" => $"TRWALE usunac caly folder '{S("folder")}' wraz z notatkami? Tej operacji nie mozna cofnac.",
            "resolve_gap" => $"Odrzucic luke w wiedzy ({S("path")})?",
            "move_note" => $"Przeniesc notatke {S("noteId")}"
                + (string.IsNullOrEmpty(S("targetFolder")) ? "" : $" do folderu '{S("targetFolder")}'")
                + (string.IsNullOrEmpty(S("newParentId")) ? "" : $", nowy rodzic: {S("newParentId")}")
                + "?",
            "bulk_trash" => $"Przeniesc {SArr("noteIds").Length} notatek z folderu '{S("folder")}' do kosza?",
            "bulk_move" => $"Przeniesc {SArr("noteIds").Length} notatek"
                + (string.IsNullOrEmpty(S("targetFolder")) ? "" : $" do folderu '{S("targetFolder")}'")
                + (string.IsNullOrEmpty(S("newParentId")) ? "" : $", nowy rodzic: {S("newParentId")}")
                + "?",
            "bulk_tag" => $"Zmienic tagi na {SArr("noteIds").Length} notatkach (dodaj: [{string.Join(", ", SArr("addTags"))}], usun: [{string.Join(", ", SArr("removeTags"))}])?",
            "bulk_pin" => $"{(args.GetProperty("pinned").GetBoolean() ? "Przypiac" : "Odpiac")} {SArr("noteIds").Length} notatek?",
            "purge_trash_all" => "TRWALE usunac WSZYSTKIE notatki z kosza? Tej operacji nie mozna cofnac.",
            "bulk_create_folders" => $"Utworzyc {SArr("names").Length} nowych folderow: [{string.Join(", ", SArr("names"))}]?",
            "bulk_delete_folders" => $"TRWALE usunac {SArr("folders").Length} folderow wraz z notatkami: [{string.Join(", ", SArr("folders"))}]? Tej operacji nie mozna cofnac.",
            "bulk_restore" => $"Przywrocic {SArr("trashPaths").Length} notatek z kosza?",
            "bulk_purge" => $"TRWALE usunac {SArr("trashPaths").Length} notatek z kosza? Tej operacji nie mozna cofnac.",
            "bulk_resolve_gaps" => $"Odrzucic {SArr("paths").Length} luk w wiedzy?",
            "add_glossary_entry" => $"Dodac do slownika: '{S("term")}' = \"{Truncate(S("definition"), 100)}\"?",
            "delete_glossary_entry" => $"Usunac ze slownika termin '{S("term")}'?",
            "bulk_note_create" => $"Dodac {S("notes").Split(',').Count(s => !string.IsNullOrWhiteSpace(s))} notatek do folderu '{S("folder")}'?",
            "bulk_note_update" => $"Zaktualizowac {S("updates").Split(',').Count(s => !string.IsNullOrWhiteSpace(s))} notatek w folderze '{S("folder")}'?",
            _ => $"Wykonac akcje '{toolName}' z argumentami {argsJson}?"
        };
    }

    private async Task<string> ExecuteToolAsync(string toolName, string argsJson, CancellationToken ct)
    {
        var args = JsonDocument.Parse(argsJson).RootElement;
        string Req(string name) => args.GetProperty(name).GetString()!;
        string? Opt(string name) => args.TryGetProperty(name, out var v) ? v.GetString() : null;
        string[] ReqArr(string name) => args.GetProperty(name).EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
        string[] OptArr(string name) => args.TryGetProperty(name, out var v) ? v.EnumerateArray().Select(e => e.GetString() ?? "").ToArray() : [];

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
                var parentId = Opt("parentId") is { } p ? Guid.Parse(p) : (Guid?)null;
                if (parentId is { } pid && !(await noteStore.ListAsync(folder, ct)).Any(n => n.Id == pid))
                    return $"Nie znaleziono notatki-rodzica {pid} w folderze '{folder}'.";

                var now = DateTimeOffset.UtcNow;

                var result = await compressor.CompressAsync(text, ct);
                var note = new Note(Guid.NewGuid(), result.Title, text, result.CompressedContent, result.Tags, now, now, ParentId: parentId);
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
                    {
                        reply += $" UWAGA - mozliwa sprzecznosc z \"{conflict.ConflictingTitle}\": {conflict.Explanation}";

                        if ((await noteStore.ListFactHistoryAsync(conflict.ConflictingTitle!, ct)).Count == 0)
                        {
                            var original = relatedNotes.First(n => n.Title == conflict.ConflictingTitle);
                            await noteStore.RecordFactVersionAsync(conflict.ConflictingTitle!, original.CompressedContent, original.Title, ct);
                        }
                        await noteStore.RecordFactVersionAsync(conflict.ConflictingTitle!, note.CompressedContent, result.Title, ct);
                    }
                }

                var closedGaps = await gapAutoCloser.TryCloseMatchingGapsAsync(ct);
                if (closedGaps > 0)
                    reply += $" Zamknieto rowniez {closedGaps} luk(i) w wiedzy.";

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

            case "move_note":
            {
                var folder = Req("folder");
                var noteId = Guid.Parse(Req("noteId"));
                var targetFolder = Opt("targetFolder");
                var newParentIdRaw = Opt("newParentId");

                if (string.IsNullOrEmpty(targetFolder) && string.IsNullOrEmpty(newParentIdRaw))
                    return "Podaj targetFolder i/lub newParentId - nie ma czego zmieniac.";

                return await MoveNoteCoreAsync(folder, noteId, targetFolder, newParentIdRaw, ct);
            }

            case "bulk_trash":
            {
                var folder = Req("folder");
                var notes = await noteStore.ListAsync(folder, ct);
                var results = new List<string>();
                foreach (var noteId in ReqArr("noteIds").Select(Guid.Parse))
                {
                    var note = notes.FirstOrDefault(n => n.Id == noteId);
                    if (note is null) { results.Add($"Nie znaleziono notatki {noteId}."); continue; }

                    await vectorIndex.DeleteNoteAsync(folder, noteId, ct);
                    await noteStore.MoveToTrashAsync(folder, note.FilePath, ct);
                    results.Add($"Do kosza: {note.Title}");
                }
                return string.Join("\n", results);
            }

            case "bulk_move":
            {
                var folder = Req("folder");
                var targetFolder = Opt("targetFolder");
                var newParentIdRaw = Opt("newParentId");
                if (string.IsNullOrEmpty(targetFolder) && string.IsNullOrEmpty(newParentIdRaw))
                    return "Podaj targetFolder i/lub newParentId - nie ma czego zmieniac.";

                var results = new List<string>();
                foreach (var noteId in ReqArr("noteIds").Select(Guid.Parse))
                    results.Add(await MoveNoteCoreAsync(folder, noteId, targetFolder, newParentIdRaw, ct));
                return string.Join("\n", results);
            }

            case "bulk_tag":
            {
                var folder = Req("folder");
                var addTags = OptArr("addTags");
                var removeTags = OptArr("removeTags");
                var notes = await noteStore.ListAsync(folder, ct);

                var results = new List<string>();
                foreach (var noteId in ReqArr("noteIds").Select(Guid.Parse))
                {
                    var existing = notes.FirstOrDefault(n => n.Id == noteId);
                    if (existing is null) { results.Add($"Nie znaleziono notatki {noteId}."); continue; }

                    var newTags = existing.Tags
                        .Where(t => !removeTags.Contains(t, StringComparer.OrdinalIgnoreCase))
                        .Concat(addTags)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    var note = existing with { Tags = newTags, UpdatedAt = DateTimeOffset.UtcNow };
                    var path = await noteStore.SaveAsync(folder, note, ct);
                    note = note with { FilePath = path };

                    var vector = await embedder.EmbedAsync(note.CompressedContent, ct);
                    await vectorIndex.UpsertAsync(folder, note, vector, ct);
                    results.Add($"Zaktualizowano tagi: {note.Title}");
                }
                return string.Join("\n", results);
            }

            case "bulk_pin":
            {
                var folder = Req("folder");
                var pinned = args.GetProperty("pinned").GetBoolean();
                var notes = await noteStore.ListAsync(folder, ct);

                var results = new List<string>();
                foreach (var noteId in ReqArr("noteIds").Select(Guid.Parse))
                {
                    var existing = notes.FirstOrDefault(n => n.Id == noteId);
                    if (existing is null) { results.Add($"Nie znaleziono notatki {noteId}."); continue; }

                    var note = existing with { Pinned = pinned, UpdatedAt = DateTimeOffset.UtcNow };
                    var path = await noteStore.SaveAsync(folder, note, ct);
                    note = note with { FilePath = path };

                    var vector = await embedder.EmbedAsync(note.CompressedContent, ct);
                    await vectorIndex.UpsertAsync(folder, note, vector, ct);
                    results.Add(pinned ? $"Przypieto: {note.Title}" : $"Odpieto: {note.Title}");
                }
                return string.Join("\n", results);
            }

            case "purge_trash_all":
            {
                var trashed = await noteStore.ListTrashAsync(ct);
                foreach (var t in trashed)
                    await noteStore.PurgeTrashAsync(t.TrashPath, ct);
                return $"Trwale usunieto {trashed.Count} notatek z kosza.";
            }

            case "bulk_create_folders":
            {
                var results = new List<string>();
                foreach (var name in ReqArr("names"))
                {
                    var created = await vectorIndex.CreateFolderAsync(name, ct);
                    results.Add(created ? $"Utworzono folder '{name}'." : $"Folder '{name}' juz istnieje.");
                }
                return string.Join("\n", results);
            }

            case "bulk_delete_folders":
            {
                var results = new List<string>();
                foreach (var folder in ReqArr("folders"))
                {
                    await vectorIndex.DeleteFolderAsync(folder, ct);
                    await noteStore.DeleteFolderAsync(folder, ct);
                    results.Add($"Usunieto folder '{folder}' trwale.");
                }
                return string.Join("\n", results);
            }

            case "bulk_restore":
            {
                var results = new List<string>();
                foreach (var trashPath in ReqArr("trashPaths"))
                {
                    var restored = await noteStore.RestoreFromTrashAsync(trashPath, ct);
                    var vector = await embedder.EmbedAsync(restored.Note.CompressedContent, ct);
                    await vectorIndex.UpsertAsync(restored.OriginalFolder, restored.Note, vector, ct);
                    results.Add($"Przywrocono '{restored.Note.Title}' do folderu '{restored.OriginalFolder}'.");
                }
                return string.Join("\n", results);
            }

            case "bulk_purge":
            {
                foreach (var trashPath in ReqArr("trashPaths"))
                    await noteStore.PurgeTrashAsync(trashPath, ct);
                return $"Trwale usunieto {ReqArr("trashPaths").Length} notatek z kosza.";
            }

            case "bulk_resolve_gaps":
            {
                foreach (var path in ReqArr("paths"))
                    await noteStore.ResolveGapAsync(path, ct);
                return $"Odrzucono {ReqArr("paths").Length} luk w wiedzy.";
            }

            case "add_glossary_entry":
            {
                var term = Req("term");
                await noteStore.SaveGlossaryEntryAsync(term, Req("definition"), "Reczny wpis", ct);
                return $"Dodano do slownika: {term}";
            }

            case "delete_glossary_entry":
            {
                var term = Req("term");
                var deleted = await noteStore.DeleteGlossaryEntryAsync(term, ct);
                return deleted ? $"Usunieto ze slownika: {term}" : $"Nie znaleziono terminu '{term}' w slowniku.";
            }

            case "export_folder":
            {
                var folder = Req("folder");
                var notes = await noteStore.ListAsync(folder, ct);
                if (notes.Count == 0)
                    return $"Folder '{folder}' jest pusty.";

                var sb = new StringBuilder();
                foreach (var note in notes.OrderBy(n => n.Title))
                {
                    sb.AppendLine($"# {note.Title}");
                    sb.AppendLine();
                    sb.AppendLine(note.RawContent);
                    sb.AppendLine();
                    sb.AppendLine("---");
                    sb.AppendLine();
                }
                return sb.ToString();
            }

            case "edit_note":
            {
                var folder = Req("folder");
                var notes = await noteStore.ListAsync(folder, ct);
                return await EditNoteCoreAsync(folder, notes, Guid.Parse(Req("noteId")), Req("text"), ct);
            }

            case "bulk_note_update":
            {
                var folder = Req("folder");
                var notes = await noteStore.ListAsync(folder, ct);

                var results = new List<string>();
                foreach (var entry in Req("updates").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
                {
                    var sep = entry.IndexOf(':');
                    if (sep < 0) { results.Add($"Zly format wpisu: '{entry}' (oczekiwano 'noteId: tekst')."); continue; }

                    var idPart = entry[..sep].Trim();
                    var text = entry[(sep + 1)..].Trim();
                    if (!Guid.TryParse(idPart, out var noteId)) { results.Add($"Niepoprawne id: '{idPart}'."); continue; }

                    results.Add(await EditNoteCoreAsync(folder, notes, noteId, text, ct));
                }
                return string.Join("\n", results);
            }

            case "set_note_pinned":
            {
                var folder = Req("folder");
                var noteId = Guid.Parse(Req("noteId"));
                var pinned = args.GetProperty("pinned").GetBoolean();

                var notes = await noteStore.ListAsync(folder, ct);
                var existing = notes.FirstOrDefault(n => n.Id == noteId);
                if (existing is null)
                    return $"Nie znaleziono notatki {noteId} w folderze '{folder}'.";

                var note = existing with { Pinned = pinned, UpdatedAt = DateTimeOffset.UtcNow };
                var path = await noteStore.SaveAsync(folder, note, ct);
                note = note with { FilePath = path };

                var vector = await embedder.EmbedAsync(note.CompressedContent, ct);
                await vectorIndex.UpsertAsync(folder, note, vector, ct);

                return pinned ? $"Przypieto: {note.Title}" : $"Odpieto: {note.Title}";
            }

            case "bulk_import":
            {
                var lines = args.GetProperty("lines").EnumerateArray()
                    .Select(e => (e.GetString() ?? "").Trim()).Where(l => l.Length > 0).ToList();
                return await BulkCreateNotesAsync(Req("folder"), lines, ct);
            }

            case "bulk_note_create":
            {
                var lines = Req("notes").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                return await BulkCreateNotesAsync(Req("folder"), lines, ct);
            }

            case "list_templates":
                return JsonSerializer.Serialize(await noteStore.ListTemplatesAsync(ct));

            case "fact_history":
                return JsonSerializer.Serialize(await noteStore.ListFactHistoryAsync(Req("subject"), ct));

            case "list_by_tag":
            {
                var tag = Req("tag");
                var folders = Opt("folder") is { } f ? (IReadOnlyList<string>)[f] : await vectorIndex.ListFoldersAsync(ct);

                var matches = new List<object>();
                foreach (var folder in folders)
                {
                    var notes = await noteStore.ListAsync(folder, ct);
                    matches.AddRange(notes.Where(n => n.Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
                        .Select(n => new { Folder = folder, n.Id, n.Title, n.Tags }));
                }

                return JsonSerializer.Serialize(matches);
            }

            case "get_note_tree":
            {
                var notes = await noteStore.ListAsync(Req("folder"), ct);

                object BuildNode(Note n) => new
                {
                    n.Id,
                    n.Title,
                    Children = notes.Where(c => c.ParentId == n.Id).Select(BuildNode).ToList()
                };

                return JsonSerializer.Serialize(notes.Where(n => n.ParentId is null).Select(BuildNode).ToList());
            }

            case "find_duplicate_tags":
            {
                var allTags = new List<string>();
                foreach (var folder in await vectorIndex.ListFoldersAsync(ct))
                    foreach (var note in await noteStore.ListAsync(folder, ct))
                        allTags.AddRange(note.Tags);

                var groups = await tagCleaner.FindDuplicateGroupsAsync(allTags.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), ct);
                return JsonSerializer.Serialize(groups);
            }

            case "merge_tags":
            {
                var fromTags = args.GetProperty("fromTags").EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
                var toTag = Req("toTag");
                var count = await tagMerger.MergeAsync(fromTags, toTag, ct);
                return $"Scalono tagi w '{toTag}' - zaktualizowano {count} notatek.";
            }

            case "find_duplicate_notes":
            {
                var threshold = args.TryGetProperty("threshold", out var t) ? (float)t.GetDouble() : 0.92f;
                var dups = await duplicateScanner.FindCrossFolderDuplicatesAsync(threshold, ct);
                return JsonSerializer.Serialize(dups.Select(d => new
                {
                    A = new { d.A.Id, d.A.Title },
                    B = new { d.B.Id, d.B.Title },
                    d.Similarity
                }));
            }

            default:
                return $"Nieznane narzedzie: {toolName}";
        }
    }

    private async Task<List<(string Folder, ScoredNote Scored)>> SearchAcrossAsync(string query, string? folder, CancellationToken ct)
    {
        var queryVector = await embedder.EmbedAsync(query, ct);
        IReadOnlyList<string> folders = folder is not null ? [folder] : await vectorIndex.ListFoldersAsync(ct);

        var candidates = await HybridNoteSearch.SearchFoldersAsync(vectorIndex, noteStore, folders, query, queryVector, vectorLimit: 10, ct);

        var folderById = candidates.ToDictionary(c => c.Scored.Note.Id, c => c.Folder);
        var reranked = await reranker.RerankAsync(query, candidates.Select(c => c.Scored).ToList(), ct);
        return reranked.Select(r => (folderById.GetValueOrDefault(r.Note.Id, folder ?? ""), r)).ToList();
    }

    private async Task<string> EditNoteCoreAsync(string folder, IReadOnlyList<Note> notes, Guid noteId, string text, CancellationToken ct)
    {
        var existing = notes.FirstOrDefault(n => n.Id == noteId);
        if (existing is null)
            return $"Nie znaleziono notatki {noteId} w folderze '{folder}'.";

        var result = await compressor.CompressAsync(text, ct);
        var note = existing with
        {
            Title = result.Title,
            RawContent = text,
            CompressedContent = result.CompressedContent,
            Tags = result.Tags,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var path = await noteStore.SaveAsync(folder, note, ct);
        note = note with { FilePath = path };

        foreach (var def in result.Definitions ?? [])
            await noteStore.SaveGlossaryEntryAsync(def.Term, def.Definition, result.Title, ct);

        var vector = await embedder.EmbedAsync(note.CompressedContent, ct);
        await vectorIndex.UpsertAsync(folder, note, vector, ct);

        return $"Zaktualizowano notatke: {note.Title}";
    }

    private async Task<string> BulkCreateNotesAsync(string folder, List<string> lines, CancellationToken ct)
    {
        if (lines.Count == 0)
            return "Brak niepustych notatek do zaimportowania.";

        var imported = 0;
        foreach (var line in lines)
        {
            var now = DateTimeOffset.UtcNow;
            var result = await compressor.CompressAsync(line, ct);
            var note = new Note(Guid.NewGuid(), result.Title, line, result.CompressedContent, result.Tags, now, now);
            var path = await noteStore.SaveAsync(folder, note, ct);
            note = note with { FilePath = path };

            foreach (var def in result.Definitions ?? [])
                await noteStore.SaveGlossaryEntryAsync(def.Term, def.Definition, result.Title, ct);

            var vector = await embedder.EmbedAsync(note.CompressedContent, ct);
            await vectorIndex.UpsertAsync(folder, note, vector, ct);
            imported++;
        }

        var closedGaps = await gapAutoCloser.TryCloseMatchingGapsAsync(ct);
        return closedGaps > 0
            ? $"Zaimportowano {imported} notatek do '{folder}'. Zamknieto {closedGaps} luk(i) w wiedzy."
            : $"Zaimportowano {imported} notatek do '{folder}'.";
    }

    private async Task<string> MoveNoteCoreAsync(string folder, Guid noteId, string? targetFolder, string? newParentIdRaw, CancellationToken ct)
    {
        var notes = await noteStore.ListAsync(folder, ct);
        var note = notes.FirstOrDefault(n => n.Id == noteId);
        if (note is null)
            return $"Nie znaleziono notatki {noteId} w folderze '{folder}'.";

        var movingFolder = !string.IsNullOrEmpty(targetFolder) && targetFolder != folder;
        if (movingFolder && notes.Any(n => n.ParentId == noteId))
            return "Ta notatka ma podnotatki - przenoszenie miedzy folderami z podnotatkami nie jest wspierane. Najpierw odepnij/przenies dzieci.";

        var newParentId = note.ParentId;
        if (newParentIdRaw is not null)
        {
            if (newParentIdRaw == "root")
            {
                newParentId = null;
            }
            else
            {
                var parentId = Guid.Parse(newParentIdRaw);
                if (parentId == noteId)
                    return "Notatka nie moze byc wlasnym rodzicem.";

                var parentScopeNotes = movingFolder ? await noteStore.ListAsync(targetFolder!, ct) : notes;
                if (parentScopeNotes.All(n => n.Id != parentId))
                    return $"Nie znaleziono notatki-rodzica {parentId} w folderze docelowym.";
                if (!movingFolder && IsDescendant(notes, noteId, parentId))
                    return "Nie mozna zagniezdzic notatki pod jej wlasnym potomkiem.";

                newParentId = parentId;
            }
        }

        var effectiveFolder = movingFolder ? targetFolder! : folder;
        var updated = note with { ParentId = newParentId, UpdatedAt = DateTimeOffset.UtcNow };

        if (movingFolder)
        {
            var newPath = await noteStore.MoveAsync(folder, targetFolder!, updated, ct);
            updated = updated with { FilePath = newPath };
            await vectorIndex.DeleteNoteAsync(folder, noteId, ct);
        }
        else
        {
            var path = await noteStore.SaveAsync(folder, updated, ct);
            updated = updated with { FilePath = path };
        }

        var vector = await embedder.EmbedAsync(updated.CompressedContent, ct);
        await vectorIndex.UpsertAsync(effectiveFolder, updated, vector, ct);

        return movingFolder
            ? $"Przeniesiono '{updated.Title}' do folderu '{targetFolder}'."
            : $"Zaktualizowano rodzica notatki '{updated.Title}'.";
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "...";

    private static bool IsDescendant(IReadOnlyList<Note> notes, Guid ancestorId, Guid candidateId)
    {
        var current = notes.FirstOrDefault(n => n.Id == candidateId);
        while (current?.ParentId is { } parentId)
        {
            if (parentId == ancestorId)
                return true;
            current = notes.FirstOrDefault(n => n.Id == parentId);
        }
        return false;
    }
}
