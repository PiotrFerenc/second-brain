using System.Globalization;
using Avalonia.Data.Converters;

namespace SecondBrain.Desktop.Converters;

public class PinLabelConverter : IValueConverter
{
    public static readonly PinLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "Odepnij" : "Przypnij";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
