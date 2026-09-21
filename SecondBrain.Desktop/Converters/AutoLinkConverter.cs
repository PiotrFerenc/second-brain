using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Data.Converters;

namespace SecondBrain.Desktop.Converters;

// Markdown.Avalonia nie autolinkuje golych URLi wklejonych jako zwykly tekst (nawet nie
// CommonMark-owe <url>) - notatki czesto maja linki wklejone tak wlasnie, wiec owijamy je
// w [url](url) zeby MarkdownScrollViewer wyrenderowal je jako klikalny hyperlink.
public class AutoLinkConverter : IValueConverter
{
    public static readonly AutoLinkConverter Instance = new();

    private static readonly Regex UrlPattern = new(@"https?://\S+", RegexOptions.Compiled);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || text.Length == 0)
            return value;

        return UrlPattern.Replace(text, m =>
        {
            var url = m.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}', '\'', '"');
            var trailing = m.Value[url.Length..];
            return $"[{url}]({url}){trailing}";
        });
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
