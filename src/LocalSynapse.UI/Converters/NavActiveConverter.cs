using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LocalSynapse.UI.Converters;

/// <summary>
/// Converts current PageType to visual properties for sidebar navigation.
/// Parameter format: "PageName:Property" where Property is Bg, Fg, or Bar.
/// Examples: "Search:Bg", "DataSetup:Fg", "Settings:Bar"
/// </summary>
public sealed class NavActiveConverter : IValueConverter
{
    public static readonly NavActiveConverter Instance = new();

    private static readonly SolidColorBrush ActiveBg = new(Color.Parse("#EFF6FF"));
    private static readonly SolidColorBrush ActiveFg = new(Color.Parse("#2563EB"));
    private static readonly SolidColorBrush InactiveFg = new(Color.Parse("#9CA3AF"));
    private static readonly SolidColorBrush Transparent = new(Colors.Transparent);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ViewModels.PageType current || parameter is not string param)
            return Transparent;

        var parts = param.Split(':');
        if (parts.Length < 2 || !Enum.TryParse<ViewModels.PageType>(parts[0], out var target))
            return Transparent;

        bool isActive = current == target;
        return parts[1] switch
        {
            "Bg" => isActive ? ActiveBg : Transparent,
            "Fg" => isActive ? (object)ActiveFg : InactiveFg,
            "Bar" => isActive,
            _ => Transparent
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
