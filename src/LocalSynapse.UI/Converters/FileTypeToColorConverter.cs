using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LocalSynapse.UI.Converters;

/// <summary>
/// Converts file extension to foreground/background color pair.
/// Pass "Bg" as parameter for background, otherwise returns foreground.
///
/// Design decision: ALL file types use the same neutral gray badge.
/// Only folders use brand blue. This reduces visual noise and keeps
/// focus on filenames rather than type badges.
/// </summary>
public sealed class FileTypeToColorConverter : IValueConverter
{
    /// <summary>Singleton instance for XAML usage.</summary>
    public static readonly FileTypeToColorConverter Instance = new();

    // Folder = Brand blue
    private static readonly SolidColorBrush FolderFg = new(Color.Parse("#2563EB"));
    private static readonly SolidColorBrush FolderBg = new(Color.Parse("#EFF6FF"));

    // All file types = Neutral gray (unified)
    private static readonly SolidColorBrush FileFg = new(Color.Parse("#6B7280"));
    private static readonly SolidColorBrush FileBg = new(Color.Parse("#F0F1F3"));

    /// <summary>Convert extension string to SolidColorBrush.</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isBg = parameter is string s && s.Equals("Bg", StringComparison.OrdinalIgnoreCase);
        string ext = value switch
        {
            string v when v.StartsWith('.') => v.ToLowerInvariant(),
            string v => Path.GetExtension(v).ToLowerInvariant(),
            _ => string.Empty
        };

        // Folder uses brand blue, everything else is neutral
        if (ext == ".folder")
            return isBg ? FolderBg : FolderFg;

        return isBg ? (object)FileBg : FileFg;
    }

    /// <summary>Not supported.</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
