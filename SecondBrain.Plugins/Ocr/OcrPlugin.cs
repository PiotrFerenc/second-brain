using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecondBrain.Core;
using SecondBrain.Infrastructure;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Ocr;

// LightOnOCR-2-1B to zwykle self-hosted serwer (np. vLLM), nie publiczne SaaS jak OpenAI -
// BaseAddress pusty domyslnie, do uzupelnienia per-maszyna (ten sam wzorzec co Reranker/Cohere).
public class OcrOptions : HttpClientOptions
{
    public string Model { get; set; } = "LightOnOCR-2-1B";

    public string Prompt { get; set; } =
        "Przepisz caly tekst widoczny na tym obrazku, doslownie, bez komentarzy. Sformatuj wynik jako czytelny markdown (naglowki, listy, pogrubienia, akapity zgodnie ze struktura tekstu na obrazku), zachowujac oryginalna tresc bez zmian.";
}

// OCR obrazka ze schowka: do edytora (nowa notatka), jako zapytanie Szukaj i z menu tray.
// Wyciagniety tekst NIE zapisuje sie sam - OCR bywa niedokladny, user poprawia przed "Zapisz".
public sealed class OcrPlugin : IPlugin
{
    public string Id => "ocr";
    public string Name => "OCR";
    public string Description => "Odczyt tekstu z obrazka ze schowka: do notatki, jako zapytanie lub z menu tray.";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.Configure<OcrOptions>(config.GetSection("Ocr"));
        services.AddHttpClient("Ocr", (sp, client) =>
            HttpClientHeaders.Apply(client, sp.GetRequiredService<IOptions<OcrOptions>>().Value));
        services.AddSingleton<IOcrExtractor, LightOnOcrExtractor>();

        services.AddSingleton<ISlotContribution, OcrToEditorAction>();
        services.AddSingleton<ISlotContribution, OcrToSearchAction>();
        services.AddSingleton<ITrayNewNoteContribution, OcrTrayItem>();
    }
}
