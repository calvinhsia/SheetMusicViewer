using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace SheetMusicViewer.Desktop;

/// <summary>
/// Converts boolean to star character for displaying favorites
/// </summary>
public class BoolToStarConverter : IValueConverter
{
    public static readonly BoolToStarConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue && boolValue)
        {
            return "★";
        }
        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // One-way converter - return unchanged value
        return value;
    }
}
