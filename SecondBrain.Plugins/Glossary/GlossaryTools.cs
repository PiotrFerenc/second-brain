using System.Text.Json;
using SecondBrain.Core;
using SecondBrain.Infrastructure.AgentTools;

namespace SecondBrain.Plugins.Glossary;

// Narzedzia agenta pluginu slownika - wylaczony plugin = agent ich nie widzi.

public sealed class ListGlossaryTool(GlossaryStore store) : AgentTool
{
    public override string Name => "list_glossary";
    public override string Description => "Wylistuj slownik pojec zbudowany automatycznie z notatek.";
    protected override string Parameters => """{"type":"object","properties":{}}""";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default) =>
        JsonSerializer.Serialize(await store.ListAsync(ct));
}

public sealed class AddGlossaryEntryTool(GlossaryStore store) : AgentTool
{
    public override string Name => "add_glossary_entry";
    public override string Description => "Dodaj recznie wpis do slownika pojec (albo nadpisz istniejacy o tym samym terminie).";
    protected override string Parameters => """{"type":"object","properties":{"term":{"type":"string"},"definition":{"type":"string"}},"required":["term","definition"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"Dodac do slownika: '{S(args, "term")}' = \"{Truncate(S(args, "definition"), 100)}\"?";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var term = args.Req("term");
        await store.SaveAsync(term, args.Req("definition"), "Reczny wpis", ct);
        return $"Dodano do slownika: {term}";
    }
}

public sealed class DeleteGlossaryEntryTool(GlossaryStore store) : AgentTool
{
    public override string Name => "delete_glossary_entry";
    public override string Description => "Usun wpis ze slownika pojec po terminie.";
    protected override string Parameters => """{"type":"object","properties":{"term":{"type":"string"}},"required":["term"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"Usunac ze slownika termin '{S(args, "term")}'?";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var term = args.Req("term");
        var deleted = await store.DeleteAsync(term, ct);
        return deleted ? $"Usunieto ze slownika: {term}" : $"Nie znaleziono terminu '{term}' w slowniku.";
    }
}
