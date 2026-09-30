using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecondBrain.Core;
using SecondBrain.Infrastructure;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Conflicts;

// Wykrywacz sprzecznosci faktow + wersjonowanie faktow (.facts/). Bez UI: komunikat trafia
// do Notices zapisu notatki, historia przez fact-history (CLI/agent). Wlasna sekcja configu
// "ConflictDetection" i wlasny named HttpClient, jak kazdy provider (PLAN.md sekcja 3).
public sealed class ConflictsPlugin : IPlugin
{
    public string Id => "conflicts";
    public string Name => "Sprzeczności";
    public string Description => "Ostrzega, gdy nowa notatka przeczy istniejącej, i zapisuje obie wersje faktu.";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.Configure<ConflictDetectionOptions>(config.GetSection("ConflictDetection"));
        services.AddHttpClient("ConflictDetection", (sp, client) =>
            HttpClientHeaders.Apply(client, sp.GetRequiredService<IOptions<ConflictDetectionOptions>>().Value));
        services.AddSingleton<ConflictDetector>();
        services.AddSingleton<FactStore>();
        services.AddSingleton<IAgentTool, FactHistoryTool>();
        services.AddSingleton<IEventHandler<NoteAdded>, ConflictOnNoteAdded>();
    }
}

public class ConflictDetectionOptions : HttpClientOptions
{
    // Wykrywanie sprzecznosci to realne zadanie rozumowania, nie streszczanie -
    // gpt-3.5-turbo myli sie tu nawet przy temperature=0 (zmierzone: ~2/3 trafien
    // na tym samym przykladzie). gpt-5 rozwiazuje to poprawnie za kazdym razem.
    public string Model { get; set; } = "gpt-5";

    public string SystemPrompt { get; set; } =
        "Porownujesz NOWA notatke z lista JUZ ISTNIEJACYCH notatek uzytkownika. Sprawdz, " +
        "czy ktoras z istniejacych notatek podaje INNA wartosc dla tego samego faktu " +
        "(np. inna godzina/sala/data/liczba dla tego samego wydarzenia lub tematu) niz " +
        "NOWA notatka. Zwroc WYLACZNIE obiekt JSON o polach: \"hasConflict\" (bool), " +
        "\"conflictingTitle\" (tytul sprzecznej notatki albo null jesli brak), " +
        "\"explanation\" (jedno krotkie zdanie po polsku opisujace sprzecznosc, albo " +
        "null jesli brak). Nie zgaduj - hasConflict=true tylko gdy sprzecznosc faktow " +
        "jest jednoznaczna, nie przy zwyklej roznicy tematu.";
}

// Tylko pojedyncze dodanie (nie import: N linii = N wywolan LLM, 2N byloby za drogie).
public sealed class ConflictOnNoteAdded(ConflictDetector conflictDetector, FactStore facts) : IEventHandler<NoteAdded>
{
    public async Task HandleAsync(NoteAdded e, CancellationToken ct = default)
    {
        if (e.FromImport || e.Related.Count == 0)
            return;

        var conflict = await conflictDetector.DetectAsync(e.Note.CompressedContent, e.Related, ct);
        if (!conflict.HasConflict)
            return;

        e.Notices.Add($"UWAGA - mozliwa sprzecznosc z \"{conflict.ConflictingTitle}\": {conflict.Explanation}");

        // Pierwsza sprzecznosc dla tematu: dopisz tez PIERWOTNA wersje, zeby historia
        // od razu miala obie strony.
        if ((await facts.ListFactHistoryAsync(conflict.ConflictingTitle!, ct)).Count == 0)
        {
            var original = e.Related.First(n => n.Title == conflict.ConflictingTitle);
            await facts.RecordFactVersionAsync(conflict.ConflictingTitle!, original.CompressedContent, original.Title, ct);
        }
        await facts.RecordFactVersionAsync(conflict.ConflictingTitle!, e.Note.CompressedContent, e.Result.Title, ct);
    }
}
