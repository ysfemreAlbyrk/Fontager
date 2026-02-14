using System;
using Microsoft.UI.Xaml.Data;

namespace Fontager.Viewer.Converters;

/// <summary>
/// Converts a numeric font weight (100-900) to a human-readable name.
/// </summary>
public sealed class FontWeightToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int weight)
        {
            return weight switch
            {
                100 => "Thin (100)",
                200 => "ExtraLight (200)",
                300 => "Light (300)",
                400 => "Regular (400)",
                500 => "Medium (500)",
                600 => "SemiBold (600)",
                700 => "Bold (700)",
                800 => "ExtraBold (800)",
                900 => "Black (900)",
                _ => $"Weight ({weight})"
            };
        }
        return "Unknown";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
