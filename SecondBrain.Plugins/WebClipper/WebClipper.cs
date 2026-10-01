using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SecondBrain.Infrastructure;

namespace SecondBrain.Plugins.WebClipper;

public class WebClipperOptions : HttpClientOptions
{
    public string Model { get; set; } = "gpt-3.5-turbo";

    public string SystemPrompt { get; set; } =
        "Dostajesz surowy tekst strony WWW. Zwroc glowna tresc artykulu jako czytelny markdown (naglowki, listy, akapity), " +
        "bez menu, reklam, stopek i komentarzy. Zachowaj oryginalny jezyk i tresc, nic nie dodawaj. Pierwsza linia: naglowek # z tytulem.";

    // Tyle znakow tekstu strony idzie do LLM - dluzsze strony sa ucinane.
    public int MaxChars { get; set; } = 30000;
}

// Pobiera strone (nazwany klient "WebClipperFetch", bez BaseAddress - dowolny URL) i oddaje
// ja do LLM (nazwany klient "WebClipper") do wyczyszczenia na markdown z linia zrodla na koncu.
public partial class WebClipper(IHttpClientFactory httpClientFactory, IOptions<WebClipperOptions> options)
{
    private readonly WebClipperOptions _options = options.Value;

    public static bool TryParseUrl(string? text, out Uri uri)
    {
        var ok = Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
        uri = ok ? u! : null!;
        return ok;
    }

    public async Task<string> ClipAsync(string url, CancellationToken ct = default)
    {
        if (!TryParseUrl(url, out var uri))
            throw new ArgumentException($"Niepoprawny adres URL (wymagany http/https): {url}");

        var html = await httpClientFactory.CreateClient("WebClipperFetch").GetStringAsync(uri, ct);
        var text = HtmlToText(html);
        if (text.Length > _options.MaxChars)
            text = text[.._options.MaxChars];

        var client = httpClientFactory.CreateClient("WebClipper");
        var response = await client.PostAsJsonAsync("chat/completions", new
        {
            model = _options.Model,
            messages = new object[]
            {
                new { role = "system", content = _options.SystemPrompt },
                new { role = "user", content = text }
            }
        }, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Pusta odpowiedz chat/completions.");

        return body.Choices[0].Message.Content.Trim() + $"\n\nŹródło: {uri}";
    }

    [GeneratedRegex(@"<(script|style|noscript|nav|header|footer|aside|svg|form)\b.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Noise();

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TitleTag();

    [GeneratedRegex(@"<(br|/p|/div|/li|/h[1-6]|/tr)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEnd();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\n\s*\n+")]
    private static partial Regex BlankLines();

    // Grubo, ale bez zaleznosci: wyrzuca szum, tagi blokowe zamienia na nowe linie, reszte tagow na nic.
    // ponytail: regex zamiast parsera DOM - strony SPA renderowane JS-em daja pusty tekst; HtmlAgilityPack gdy trzeba.
    public static string HtmlToText(string html)
    {
        var title = WebUtility.HtmlDecode(TitleTag().Match(html).Groups[1].Value).Trim();
        var text = Noise().Replace(html, " ");
        text = BlockEnd().Replace(text, "\n");
        text = WebUtility.HtmlDecode(AnyTag().Replace(text, " "));
        text = BlankLines().Replace(Spaces().Replace(text, " "), "\n\n").Trim();
        return title.Length > 0 ? $"Tytul strony: {title}\n\n{text}" : text;
    }

    private record ChatResponse([property: JsonPropertyName("choices")] Choice[] Choices);
    private record Choice([property: JsonPropertyName("message")] Message Message);
    private record Message([property: JsonPropertyName("content")] string Content);
}
