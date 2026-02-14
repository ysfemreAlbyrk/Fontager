using Microsoft.UI.Xaml;
using Windows.Storage;

namespace Fontager.Viewer.Services;

/// <summary>
/// Manages persistent application settings using LocalSettings.
/// </summary>
public sealed class SettingsService
{
    private readonly ApplicationDataContainer _localSettings;

    // Setting keys
    private const string ThemeKey = "AppTheme";
    private const string DefaultPreviewTextKey = "DefaultPreviewText";
    private const string DefaultFontSizeKey = "DefaultFontSize";
    private const string LastOpenDirectoryKey = "LastOpenDirectory";
    private const string BackdropKey = "Backdrop";
    private const string ShowWaterfallKey = "ShowWaterfall";

    // Defaults
    private const string DefaultPreviewTextValue = "The quick brown fox jumps over the lazy dog. 0123456789";
    private const double DefaultFontSizeValue = 48;

    public SettingsService()
    {
        _localSettings = ApplicationData.Current.LocalSettings;
    }

    /// <summary>
    /// Gets or sets the app theme (0 = System, 1 = Light, 2 = Dark).
    /// </summary>
    public ElementTheme Theme
    {
        get
        {
            var value = _localSettings.Values[ThemeKey];
            if (value is int intVal && intVal >= 0 && intVal <= 2)
                return (ElementTheme)intVal;
            return ElementTheme.Default;
        }
        set => _localSettings.Values[ThemeKey] = (int)value;
    }

    /// <summary>
    /// Gets or sets the default preview text.
    /// </summary>
    public string DefaultPreviewText
    {
        get
        {
            var value = _localSettings.Values[DefaultPreviewTextKey];
            return value is string str && !string.IsNullOrEmpty(str) ? str : DefaultPreviewTextValue;
        }
        set => _localSettings.Values[DefaultPreviewTextKey] = value;
    }

    /// <summary>
    /// Gets or sets the default font preview size.
    /// </summary>
    public double DefaultFontSize
    {
        get
        {
            var value = _localSettings.Values[DefaultFontSizeKey];
            return value is double d ? d : DefaultFontSizeValue;
        }
        set => _localSettings.Values[DefaultFontSizeKey] = value;
    }

    /// <summary>
    /// Gets or sets the last opened directory path.
    /// </summary>
    public string LastOpenDirectory
    {
        get
        {
            var value = _localSettings.Values[LastOpenDirectoryKey];
            return value is string str ? str : string.Empty;
        }
        set => _localSettings.Values[LastOpenDirectoryKey] = value;
    }

    /// <summary>
    /// Gets or sets the backdrop type. 0 = Mica, 1 = Acrylic.
    /// </summary>
    public int Backdrop
    {
        get
        {
            var value = _localSettings.Values[BackdropKey];
            return value is int intVal && intVal >= 0 && intVal <= 1 ? intVal : 0;
        }
        set => _localSettings.Values[BackdropKey] = value;
    }

    /// <summary>
    /// Gets or sets whether waterfall view is shown in the Preview tab.
    /// </summary>
    public bool ShowWaterfall
    {
        get
        {
            var value = _localSettings.Values[ShowWaterfallKey];
            return value is not bool b || b; // default true
        }
        set => _localSettings.Values[ShowWaterfallKey] = value;
    }
}
