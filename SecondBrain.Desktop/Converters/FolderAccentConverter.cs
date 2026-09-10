using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SecondBrain.Desktop.Converters;

// Przypisuje folderowi staly (w ramach sesji) kolor z akcentow Catppuccin - odpowiednik
// kolorowych sekcji w OneNote. Kolory sa z wariantu Mocha; w jasnym motywie kropka
// wyglada nieco mniej dopasowana do reszty palety, ale to kosmetyczny szczegol.
public class FolderAccentConverter : IValueConverter
{
    public static readonly FolderAccentConverter Instance = new();

    private static readonly IBrush[] Accents =
    [
        new SolidColorBrush(Color.Parse("#cba6f7")), // mauve
        new SolidColorBrush(Color.Parse("#89b4fa")), // blue
        new SolidColorBrush(Color.Parse("#94e2d5")), // teal
        new SolidColorBrush(Color.Parse("#fab387")), // peach
        new SolidColorBrush(Color.Parse("#a6e3a1")), // green
        new SolidColorBrush(Color.Parse("#f38ba8")), // red
        new SolidColorBrush(Color.Parse("#89dceb")), // sky
        new SolidColorBrush(Color.Parse("#b4befe")), // lavender
        new SolidColorBrush(Color.Parse("#f5c2e7")), // pink
        new SolidColorBrush(Color.Parse("#f2cdcd")), // flamingo
    ];

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string name || name.Length == 0)
            return Accents[0];

        var index = Math.Abs(name.GetHashCode()) % Accents.Length;
        return Accents[index];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
