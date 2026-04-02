using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace LocalSynapse.UI.Converters;

/// <summary>
/// Converts bool to IsVisible. Pass "Invert" as parameter to negate.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool boolValue = value is true;
        bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        return invert ? !boolValue : boolValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
