using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Windows.Storage;

namespace Fontager.Viewer.Services;

/// <summary>
/// Manages persistent application settings using LocalSettings.
/// </summary>
public sealed class SettingsService
{
    private readonly ApplicationDataContainer _localSettings;

    private const string ThemeKey = "AppTheme";
    private const string DefaultPreviewTextKey = "DefaultPreviewText";
    private const string DefaultFontSizeKey = "DefaultFontSize";
    private const string LastOpenDirectoryKey = "LastOpenDirectory";
    private const string BackdropKey = "Backdrop";
    private const string ShowWaterfallKey = "ShowWaterfall";
    private const string WaterfallSizesKey = "WaterfallSizes";
    private const string ShowQuickViewKey = "ShowQuickView";
    private const string ShowPreviewControlsKey = "ShowPreviewControls";
    private const string InstallModeKey = "InstallMode"; // 0 = current user, 1 = all users (system)

    private const string DefaultPreviewTextValue = "The quick brown fox jumps over the lazy dog. 0123456789";
    private const double DefaultFontSizeValue = 32;
    private const string DefaultWaterfallSizesValue = "8,10,12,14,16,18,20,24,28,32,36,40,48,56,64,72";

    public SettingsService()
    {
        _localSettings = ApplicationData.Current.LocalSettings;
    }

    /// <summary>
    /// Resets all settings to their default values.
    /// </summary>
    public void ResetToDefaults()
    {
        _localSettings.Values.Clear();
    }

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

    public string DefaultPreviewText
    {
        get
        {
            var value = _localSettings.Values[DefaultPreviewTextKey];
            return value is string str && !string.IsNullOrEmpty(str) ? str : DefaultPreviewTextValue;
        }
        set => _localSettings.Values[DefaultPreviewTextKey] = value;
    }

    public double DefaultFontSize
    {
        get
        {
            var value = _localSettings.Values[DefaultFontSizeKey];
            return value is double d ? d : DefaultFontSizeValue;
        }
        set => _localSettings.Values[DefaultFontSizeKey] = value;
    }

    public string LastOpenDirectory
    {
        get
        {
            var value = _localSettings.Values[LastOpenDirectoryKey];
            return value is string str ? str : string.Empty;
        }
        set => _localSettings.Values[LastOpenDirectoryKey] = value;
    }

    public int Backdrop
    {
        get
        {
            var value = _localSettings.Values[BackdropKey];
            return value is int intVal && intVal >= 0 && intVal <= 1 ? intVal : 0;
        }
        set => _localSettings.Values[BackdropKey] = value;
    }

    public bool ShowWaterfall
    {
        get
        {
            var value = _localSettings.Values[ShowWaterfallKey];
            return value is not bool b || b; // default true
        }
        set => _localSettings.Values[ShowWaterfallKey] = value;
    }

    /// <summary>
    /// Gets or sets whether Quick View (character set overview) is shown in the font header.
    /// </summary>
    public bool ShowQuickView
    {
        get
        {
            var value = _localSettings.Values[ShowQuickViewKey];
            return value is not bool b || b; // default true
        }
        set => _localSettings.Values[ShowQuickViewKey] = value;
    }

    /// <summary>
    /// Gets or sets whether the preview size controls (slider) are visible.
    /// </summary>
    public bool ShowPreviewControls
    {
        get
        {
            var value = _localSettings.Values[ShowPreviewControlsKey];
            return value is not bool b || b; // default true
        }
        set => _localSettings.Values[ShowPreviewControlsKey] = value;
    }

    /// <summary>
    /// Gets or sets the font install mode: 0 = current user, 1 = all users (system).
    /// </summary>
    public int InstallMode
    {
        get
        {
            var value = _localSettings.Values[InstallModeKey];
            return value is int intVal && intVal >= 0 && intVal <= 1 ? intVal : 0;
        }
        set => _localSettings.Values[InstallModeKey] = value;
    }

    /// <summary>
    /// Comma-separated waterfall sizes (e.g. "8,12,16,24,32,48,72").
    /// </summary>
    public string WaterfallSizesRaw
    {
        get
        {
            var value = _localSettings.Values[WaterfallSizesKey];
            return value is string str && !string.IsNullOrWhiteSpace(str) ? str : DefaultWaterfallSizesValue;
        }
        set => _localSettings.Values[WaterfallSizesKey] = value;
    }

    /// <summary>
    /// Parsed waterfall sizes as int array.
    /// </summary>
    public int[] GetWaterfallSizes()
    {
        try
        {
            return WaterfallSizesRaw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var v) ? v : -1)
                .Where(v => v > 0 && v <= 200)
                .OrderBy(v => v)
                .ToArray();
        }
        catch
        {
            return [8, 12, 16, 20, 24, 32, 48, 64, 72];
        }
    }
}
