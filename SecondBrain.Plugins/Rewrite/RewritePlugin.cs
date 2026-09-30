using Avalonia.Controls;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecondBrain.Infrastructure;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Rewrite;

// Przepisywanie tresci edytora przez LLM wg instrukcji usera. Wlasna sekcja configu
// "NoteRewrite" i wlasny named HttpClient, jak kazdy provider (PLAN.md sekcja 3).
public sealed class RewritePlugin : IPlugin
{
    public string Id => "rewrite";
    public string Name => "Przepisz wg instrukcji";
    public string Description => "Pole instrukcji pod edytorem - LLM przepisuje treść notatki (styl, tłumaczenie, skrót).";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.Configure<NoteRewriteOptions>(config.GetSection("NoteRewrite"));
        services.AddHttpClient("NoteRewrite", (sp, client) =>
            HttpClientHeaders.Apply(client, sp.GetRequiredService<IOptions<NoteRewriteOptions>>().Value));
        services.AddSingleton<NoteRewriter>();
        services.AddSingleton<ISlotContribution, RewriteSlot>();
    }
}

// Pole instrukcji + "Wyslij" w slocie Editor.Footer (glowne okno i szybka notatka - kazdy
// SlotHost dostaje wlasna kontrolke). Instrukcja idzie jako system prompt, tresc notatki jako
// user - odpowiedz zastepuje tekst edytora.
public sealed class RewriteSlot(NoteRewriter rewriter, IEditorContext editor) : ISlotContribution
{
    public string SlotId => "Editor.Footer";
    public int Order => 0;

    public Control CreateControl()
    {
        var instruction = new TextBox { PlaceholderText = "Instrukcja dla LLM (np. popraw styl, przetłumacz na angielski)" };
        var send = new Button { Content = "Wyślij", Margin = new Avalonia.Thickness(8, 0, 0, 0) };
        send.Click += async (_, _) =>
        {
            send.IsEnabled = false;
            try { await RewriteAsync(instruction.Text ?? ""); }
            finally { send.IsEnabled = true; }
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(send, 1);
        grid.Children.Add(instruction);
        grid.Children.Add(send);
        return grid;
    }

    private async Task RewriteAsync(string instruction)
    {
        if (string.IsNullOrWhiteSpace(instruction) || string.IsNullOrWhiteSpace(editor.Text))
        {
            editor.Status = "Wpisz instrukcje i tresc notatki.";
            return;
        }

        editor.IsBusy = true;
        try
        {
            editor.Status = "Przetwarzam wg instrukcji...";
            editor.Text = await rewriter.RewriteAsync(instruction, editor.Text);
            editor.Status = "Tresc zastapiona odpowiedzia - sprawdz i zapisz.";
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
