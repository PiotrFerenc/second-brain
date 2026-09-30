using System.Text;
using System.Text.Json;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure.AgentTools;

public sealed class ListFoldersTool(IVectorIndex vectorIndex) : AgentTool
{
    public override string Name => "list_folders";
    public override string Description => "Wylistuj wszystkie foldery (kolekcje notatek) uzytkownika.";
    protected override string Parameters => """{"type":"object","properties":{}}""";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default) =>
        JsonSerializer.Serialize(await vectorIndex.ListFoldersAsync(ct));
}

public sealed class CreateFolderTool(NotePipeline pipeline) : AgentTool
{
    public override string Name => "create_folder";
    public override string Description => "Utworz nowy, pusty folder.";
    protected override string Parameters => """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"Utworzyc nowy folder '{S(args, "name")}'?";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var name = args.Req("name");
        var created = await pipeline.CreateFolderAsync(name, ct);
        return created ? $"Utworzono folder '{name}'." : $"Folder '{name}' juz istnieje.";
    }
}

public sealed class DeleteFolderTool(NotePipeline pipeline) : AgentTool
{
    public override string Name => "delete_folder";
    public override string Description => "Usun caly folder wraz ze wszystkimi notatkami. Nieodwracalne.";
    protected override string Parameters => """{"type":"object","properties":{"folder":{"type":"string"}},"required":["folder"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"TRWALE usunac caly folder '{S(args, "folder")}' wraz z notatkami? Tej operacji nie mozna cofnac.";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var folder = args.Req("folder");
        await pipeline.DeleteFolderAsync(folder, ct);
        return $"Usunieto folder '{folder}' trwale.";
    }
}

public sealed class BulkCreateFoldersTool(NotePipeline pipeline) : AgentTool
{
    public override string Name => "bulk_create_folders";
    public override string Description => "Utworz wiele nowych, pustych folderow naraz.";
    protected override string Parameters => """{"type":"object","properties":{"names":{"type":"array","items":{"type":"string"}}},"required":["names"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"Utworzyc {SArr(args, "names").Length} nowych folderow: [{string.Join(", ", SArr(args, "names"))}]?";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var results = new List<string>();
        foreach (var name in args.ReqArr("names"))
        {
            var created = await pipeline.CreateFolderAsync(name, ct);
            results.Add(created ? $"Utworzono folder '{name}'." : $"Folder '{name}' juz istnieje.");
        }
        return string.Join("\n", results);
    }
}

public sealed class BulkDeleteFoldersTool(NotePipeline pipeline) : AgentTool
{
    public override string Name => "bulk_delete_folders";
    public override string Description => "Usun wiele folderow naraz wraz z ich notatkami. Nieodwracalne.";
    protected override string Parameters => """{"type":"object","properties":{"folders":{"type":"array","items":{"type":"string"}}},"required":["folders"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"TRWALE usunac {SArr(args, "folders").Length} folderow wraz z notatkami: [{string.Join(", ", SArr(args, "folders"))}]? Tej operacji nie mozna cofnac.";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var results = new List<string>();
        foreach (var folder in args.ReqArr("folders"))
        {
            await pipeline.DeleteFolderAsync(folder, ct);
            results.Add($"Usunieto folder '{folder}' trwale.");
        }
        return string.Join("\n", results);
    }
}

public sealed class ExportFolderTool(INoteStore noteStore) : AgentTool
{
    public override string Name => "export_folder";
    public override string Description => "Wyeksportuj caly folder jako jeden tekst markdown (wszystkie notatki polaczone) - do skopiowania/backupu.";
    protected override string Parameters => """{"type":"object","properties":{"folder":{"type":"string"}},"required":["folder"]}""";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var folder = args.Req("folder");
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
}
