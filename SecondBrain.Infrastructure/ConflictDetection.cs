using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

public class OpenAiConflictDetector(IHttpClientFactory httpClientFactory, IOptions<OpenAiOptions> options) : IConflictDetector
{
    private const string SystemPrompt =
        "Porownujesz NOWA notatke z lista JUZ ISTNIEJACYCH notatek uzytkownika. Sprawdz, " +
        "czy ktoras z istniejacych notatek podaje INNA wartosc dla tego samego faktu " +
        "(np. inna godzina/sala/data/liczba dla tego samego wydarzenia lub tematu) niz " +
        "NOWA notatka. Zwroc WYLACZNIE obiekt JSON o polach: \"hasConflict\" (bool), " +
        "\"conflictingTitle\" (tytul sprzecznej notatki albo null jesli brak), " +
        "\"explanation\" (jedno krotkie zdanie po polsku opisujace sprzecznosc, albo " +
        "null jesli brak). Nie zgaduj - hasConflict=true tylko gdy sprzecznosc faktow " +
        "jest jednoznaczna, nie przy zwyklej roznicy tematu.";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly OpenAiOptions _options = options.Value;

    public async Task<ConflictResult> DetectAsync(string newContent, IReadOnlyList<Note> candidates, CancellationToken ct = default)
    {
        if (candidates.Count == 0)
            return new ConflictResult(false, null, null);

        var context = string.Join("\n\n---\n\n", candidates.Select((n, i) => $"Notatka {i + 1} (\"{n.Title}\"): {n.CompressedContent}"));
        var userPrompt = $"NOWA notatka: {newContent}\n\nJUZ ISTNIEJACE notatki:\n{context}";

        var client = httpClientFactory.CreateClient("OpenAI");
        var response = await client.PostAsJsonAsync("chat/completions", new
        {
            model = _options.ConflictModel,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = userPrompt }
            }
        }, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Pusta odpowiedz OpenAI chat/completions.");

        var raw = body.Choices[0].Message.Content.Trim();
        return JsonSerializer.Deserialize<ConflictResult>(raw, JsonOptions)
            ?? throw new InvalidOperationException($"Nie udalo sie sparsowac JSON z wykrywacza sprzecznosci: {raw}");
    }

    private record OpenAiChatResponse([property: JsonPropertyName("choices")] OpenAiChoice[] Choices);
    private record OpenAiChoice([property: JsonPropertyName("message")] OpenAiMessage Message);
    private record OpenAiMessage([property: JsonPropertyName("content")] string Content);
}
