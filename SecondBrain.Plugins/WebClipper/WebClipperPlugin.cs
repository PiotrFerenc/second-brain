using System.Text.Json;
using Avalonia.Controls;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecondBrain.Core;
using SecondBrain.Infrastructure;
using SecondBrain.Infrastructure.AgentTools;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.WebClipper;

// Web clipper: adres strony (ze schowka lub od agenta) -> pobranie HTML -> LLM czysci do markdown
// -> notatka. Z UI tekst laduje w edytorze do wgladu (jak OCR), agent zapisuje po potwierdzeniu.
// Wlasna sekcja configu "WebClipper" (LLM) i dwa nazwane klienty: "WebClipper" (LLM) i
// "WebClipperFetch" (pobieranie stron, bez BaseAddress).
public sealed class WebClipperPlugin : IPlugin
{
    public string Id => "webclipper";
    public string Name => "Web clipper";
    public string Description => "Zapis strony WWW jako notatki: URL ze schowka (edytor, tray) lub od agenta; LLM czyści treść do markdown.";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.Configure<WebClipperOptions>(config.GetSection("WebClipper"));
        services.AddHttpClient("WebClipper", (sp, client) =>
            HttpClientHeaders.Apply(client, sp.GetRequiredService<IOptions<WebClipperOptions>>().Value));
        services.AddHttpClient("WebClipperFetch", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 SecondBrain-WebClipper");
        });
        services.AddSingleton<WebClipper>();

        services.AddSingleton<ISlotContribution, ClipUrlToEditorAction>();
        services.AddSingleton<ITrayNewNoteContribution, ClipUrlTrayItem>();
        services.AddSingleton<IAgentTool, ClipUrlTool>();
    }
}

// Wspolne dla edytora i tray: URL ze schowka -> tekst w edytorze, bez auto-zapisu.
internal static class ClipToEditor
{
    public static async Task RunAsync(WebClipper clipper, IShell shell, IEditorContext editor)
    {
        var url = await ClipboardText.GetAsync(shell.TopLevel);
        if (!WebClipper.TryParseUrl(url, out _))
        {
            editor.Status = "W schowku nie ma adresu URL (http/https).";
            return;
        }

        editor.IsBusy = true;
        try
        {
            editor.Status = "Pobieram strone...";
            editor.Text = await clipper.ClipAsync(url!);
            editor.Status = "Strona wczytana - sprawdz i zapisz.";
        }
        catch (Exception ex)
        {
            editor.Status = $"Blad: {ex.Message}";
        }
        finally
        {
            editor.IsBusy = false;
        }
    }
}

public sealed class ClipUrlToEditorAction(WebClipper clipper, IShell shell, IEditorContext editor) : ISlotContribution
{
    public string SlotId => "Editor.Toolbar";
    public int Order => 5;

    public Control CreateControl()
    {
        var button = new Button { Content = "Wklej stronę (URL ze schowka)", Classes = { "subtleAction" } };
        button.Click += async (_, _) => await ClipToEditor.RunAsync(clipper, shell, editor);
        return button;
    }
}

public sealed class ClipUrlTrayItem(WebClipper clipper, IShell shell, IEditorContext editor) : ITrayNewNoteContribution
{
    public NativeMenuItem Build(string folder)
    {
        var item = new NativeMenuItem("Strona z URL (schowek)");
        item.Click += async (_, _) =>
        {
            if (!WebClipper.TryParseUrl(await ClipboardText.GetAsync(shell.TopLevel), out _))
                return;

            await editor.OpenQuickNoteAsync(folder);
            await ClipToEditor.RunAsync(clipper, shell, editor);
        };
        return item;
    }
}

public sealed class ClipUrlTool(WebClipper clipper, NotePipeline pipeline) : AgentTool
{
    public override string Name => "clip_url";
    public override string Description => "Pobierz strone WWW z podanego adresu, oczysc do markdown i zapisz jako notatke w folderze.";
    protected override string Parameters => """{"type":"object","properties":{"folder":{"type":"string"},"url":{"type":"string"}},"required":["folder","url"]}""";
    public override bool IsMutating => true;
    public override string Describe(JsonElement args) => $"Pobrac strone {S(args, "url")} i zapisac jako notatke w folderze '{S(args, "folder")}'?";

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var folder = args.Req("folder");
        var text = await clipper.ClipAsync(args.Req("url"), ct);
        var added = await pipeline.AddAsync(folder, text, ct: ct);
        return string.Join(" ", added.Notices.Prepend($"Zapisano strone jako notatke '{added.Note.Title}' w folderze '{folder}'."));
    }
}
