using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace Fontager.Viewer.Helpers;

/// <summary>
/// Resolves effective light/dark for app settings and solid window backdrop.
/// Uses explicit colors for solid backdrop so theme changes are not stuck on a cached brush.
/// </summary>
public static class AppThemeHelper
{
    public static readonly Windows.UI.Color SolidBackdropLight = Windows.UI.Color.FromArgb(255, 243, 243, 243);
    public static readonly Windows.UI.Color SolidBackdropDark = Windows.UI.Color.FromArgb(255, 32, 32, 32);

    public static bool IsLightTheme(ElementTheme setting, FrameworkElement? context = null)
    {
        return setting switch
        {
            ElementTheme.Light => true,
            ElementTheme.Dark => false,
            _ => ResolveSystemOrActualIsLight(context)
        };
    }

    public static Windows.UI.Color SolidBackdropColor(ElementTheme setting, FrameworkElement? context = null)
        => IsLightTheme(setting, context) ? SolidBackdropLight : SolidBackdropDark;

    /// <summary>Theme-aware color for code-built UI (Application.Current.Resources stays on the app theme).</summary>
    public static Windows.UI.Color ThemeColor(string resourceKey, bool isLight) =>
        resourceKey switch
        {
            "TextFillColorTertiaryBrush" => isLight
                ? Windows.UI.Color.FromArgb(255, 96, 96, 96)
                : Windows.UI.Color.FromArgb(255, 170, 170, 170),
            "TextFillColorSecondaryBrush" => isLight
                ? Windows.UI.Color.FromArgb(255, 100, 100, 100)
                : Windows.UI.Color.FromArgb(255, 200, 200, 200),
            "CardBackgroundFillColorDefaultBrush" => isLight
                ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
                : Windows.UI.Color.FromArgb(255, 44, 44, 44),
            "CardStrokeColorDefaultBrush" => isLight
                ? Windows.UI.Color.FromArgb(255, 224, 224, 224)
                : Windows.UI.Color.FromArgb(255, 48, 48, 48),
            _ => isLight
                ? Windows.UI.Color.FromArgb(255, 18, 18, 18)
                : Windows.UI.Color.FromArgb(255, 245, 245, 245)
        };

    private static bool ResolveSystemOrActualIsLight(FrameworkElement? context)
    {
        if (context is not null)
        {
            var actual = context.ActualTheme;
            if (actual == ElementTheme.Light)
                return true;
            if (actual == ElementTheme.Dark)
                return false;
        }

        try
        {
            var bg = new UISettings().GetColorValue(UIColorType.Background);
            return (bg.R + bg.G + bg.B) > 384;
        }
        catch
        {
            return true;
        }
    }
}
