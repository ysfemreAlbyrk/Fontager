using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Fontager.Viewer.Converters;

/// <summary>
/// Converts a boolean to Visibility. True = Visible, False = Collapsed.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var boolValue = value is true;
        if (Invert) boolValue = !boolValue;
        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
