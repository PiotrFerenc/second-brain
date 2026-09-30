using System.Text.Json;
using SecondBrain.Core;
using SecondBrain.Infrastructure;
using SecondBrain.Infrastructure.AgentTools;

namespace SecondBrain.Plugins.Search;

// Narzedzia agenta pluginu szukania - rejestrowane w SearchPlugin, wiec wylaczony plugin ich nie ma.
// NoteSearch zostaje w rdzeniu (luki, CLI).

public sealed class SearchNotesTool(NoteSearch noteSearch) : AgentTool
{
    public override string Name => "search_notes";
    public override string Description => "Semantyczne wyszukiwanie notatek pasujacych do zapytania. Bez folderu przeszukuje wszystkie foldery.";
    protected override string Parameters => """{"type":"object","properties":{"query":{"type":"string"},"folder":{"type":"string"}},"required":["query"]}""";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var folder = args.Opt("folder");
        var matches = await noteSearch.SearchAsync(args.Req("query"), folder is not null ? [folder] : null, vectorLimit: 10, ct);
        return JsonSerializer.Serialize(matches.Take(10).Select(m => new
        {
            m.Folder,
            m.Note.Id,
            m.Note.Title,
            m.Note.Tags,
            m.Score,
            Snippet = Truncate(m.Note.CompressedContent, 200)
        }));
    }
}

public sealed class AskQuestionTool(NoteSearch noteSearch, IAnswerSynthesizer answerSynthesizer, IEventBus events) : AgentTool
{
    public override string Name => "ask_question";
    public override string Description => "Zadaj pytanie do bazy notatek (RAG) - zwraca zsyntetyzowana odpowiedz na podstawie znalezionych notatek. Bez folderu przeszukuje wszystkie foldery.";
    protected override string Parameters => """{"type":"object","properties":{"query":{"type":"string"},"folder":{"type":"string"}},"required":["query"]}""";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var query = args.Req("query");
        var folder = args.Opt("folder");
        var fullNotes = (await noteSearch.SearchAsync(query, folder is not null ? [folder] : null, vectorLimit: 10, ct))
            .Take(5).Select(m => m.Note).ToList();

        if (fullNotes.Count == 0)
            return "Brak notatek pasujacych do tego pytania.";

        var answer = await answerSynthesizer.SynthesizeAsync(query, fullNotes, ct);
        await events.PublishAsync(new SearchCompleted(query, answer.Answered), ct);
        return answer.Answer;
    }
}
