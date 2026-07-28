using System.Globalization;
using System.Windows.Data;

namespace FFXIVSimpleLauncher.Converters;

/// <summary>
/// Presents a null profile binding as the empty-string ID used by the shared profile option.
/// </summary>
public sealed class NullToEmptyStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value as string ?? string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
