using System.Text.Json;
using SecondBrain.Infrastructure.AgentTools;

namespace SecondBrain.Plugins.Templates;

// Narzedzie agenta pluginu szablonow - rejestrowane w TemplatesPlugin, wiec wylaczony plugin go nie ma.
public sealed class ListTemplatesTool(TemplateStore templates) : AgentTool
{
    public override string Name => "list_templates";
    public override string Description => "Wylistuj szablony notatek (nazwa + tresc) dostepne do wykorzystania przed dodaniem notatki.";
    protected override string Parameters => """{"type":"object","properties":{}}""";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default) =>
        JsonSerializer.Serialize(await templates.ListAsync(ct));
}
