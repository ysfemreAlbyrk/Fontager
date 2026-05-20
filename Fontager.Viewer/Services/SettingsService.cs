using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.UI.Xaml;

namespace Fontager.Viewer.Services;

/// <summary>
/// Persists user settings as JSON under <c>%LocalAppData%\Fontager\settings.json</c>.
///
/// Why a hand-rolled store instead of <c>Windows.Storage.ApplicationData</c>:
/// in an unpackaged WinUI 3 build the WinRT Storage APIs depend on the
/// Windows App SDK identity bridge. It usually works, but the storage path
/// is keyed off the EXE path (so moving the EXE loses settings), and a small
/// set of host configurations short-circuits the shim entirely. A plain JSON
/// file is identical across packaging modes — so if/when we re-enable MSIX
/// for the Store later (see <c>docs/research/packaging-decision.md</c>) we
/// don't have to migrate the user's settings.
/// </summary>
public sealed class SettingsService
{
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
    private const string ExitAppAfterSuccessfulInstallKey = "ExitAppAfterSuccessfulInstall";
    private const string RunAsAdministratorKey = "RunAsAdministrator";
    private const string ElevateForAllUsersInstallKey = "ElevateForAllUsersInstall";
    private const string WindowWidthKey = "WindowWidth";
    private const string WindowHeightKey = "WindowHeight";
    private const string WindowXKey = "WindowX";
    private const string WindowYKey = "WindowY";
    private const string WindowMaximizedKey = "WindowMaximized";

    private const string DefaultPreviewTextValue = "The quick brown fox jumps over the lazy dog. 0123456789";
    private const double DefaultFontSizeValue = 32;
    private const string DefaultWaterfallSizesValue = "8,10,12,14,16,18,20,24,28,32,36,40,48,56,64,72";

    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private Dictionary<string, JsonElement> _values;

    public SettingsService()
    {
        var dir = FontagerPaths.LocalAppDataRoot;
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");
        _values = Load();
    }

    private Dictionary<string, JsonElement> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new();
            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
                return new();
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();
        }
        catch
        {
            // A corrupted settings file should never crash the app; we just
            // start from defaults next launch.
            return new();
        }
    }

    private void Save()
    {
        try
        {
            // Atomic write: write to a sibling temp file then move into place
            // so a mid-write power loss can't leave the JSON corrupted.
            var temp = _filePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_values, s_jsonOptions));
            File.Move(temp, _filePath, overwrite: true);
        }
        catch
        {
            // Best-effort. Persistence is a nice-to-have, not a correctness
            // requirement.
        }
    }

    private T? GetValue<T>(string key, T? fallback = default)
    {
        if (!_values.TryGetValue(key, out var element))
            return fallback;
        try
        {
            return element.Deserialize<T>();
        }
        catch
        {
            return fallback;
        }
    }

    private void SetValue<T>(string key, T value)
    {
        _values[key] = JsonSerializer.SerializeToElement(value);
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Resets all settings to their default values.
    /// </summary>
    public void ResetToDefaults()
    {
        _values = new();
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Fired after any setting is persisted. Listeners (typically <c>MainWindow</c>)
    /// re-apply theme / backdrop / preview state so the user sees the effect
    /// immediately while still on the Settings page — no Save button required.
    /// </summary>
    public event EventHandler? Changed;

    public ElementTheme Theme
    {
        get
        {
            var v = GetValue<int?>(ThemeKey);
            return v is >= 0 and <= 2 ? (ElementTheme)v.Value : ElementTheme.Default;
        }
        set => SetValue(ThemeKey, (int)value);
    }

    public string DefaultPreviewText
    {
        get
        {
            var s = GetValue<string>(DefaultPreviewTextKey);
            return string.IsNullOrEmpty(s) ? DefaultPreviewTextValue : s;
        }
        set => SetValue(DefaultPreviewTextKey, value ?? string.Empty);
    }

    public double DefaultFontSize
    {
        get
        {
            var v = GetValue<double?>(DefaultFontSizeKey);
            return v ?? DefaultFontSizeValue;
        }
        set => SetValue(DefaultFontSizeKey, value);
    }

    public string LastOpenDirectory
    {
        get => GetValue<string>(LastOpenDirectoryKey) ?? string.Empty;
        set => SetValue(LastOpenDirectoryKey, value ?? string.Empty);
    }

    public int Backdrop
    {
        get
        {
            var v = GetValue<int?>(BackdropKey);
            if (v is null) return 0;
            // Legacy tag 4 ("acrylic thin") matched standard acrylic; normalize reads.
            if (v == 4) return 1;
            return v is >= 0 and <= 3 ? v.Value : 0;
        }
        set => SetValue(BackdropKey, value);
    }

    public bool ShowWaterfall
    {
        get => GetValue<bool?>(ShowWaterfallKey) ?? true;
        set => SetValue(ShowWaterfallKey, value);
    }

    /// <summary>
    /// Gets or sets whether Quick View (character set overview) is shown in the font header.
    /// </summary>
    public bool ShowQuickView
    {
        get => GetValue<bool?>(ShowQuickViewKey) ?? true;
        set => SetValue(ShowQuickViewKey, value);
    }

    /// <summary>
    /// Gets or sets whether the preview size controls (slider) are visible.
    /// </summary>
    public bool ShowPreviewControls
    {
        get => GetValue<bool?>(ShowPreviewControlsKey) ?? true;
        set => SetValue(ShowPreviewControlsKey, value);
    }

    /// <summary>
    /// Gets or sets the font install mode: 0 = current user, 1 = all users (system).
    /// </summary>
    public int InstallMode
    {
        get
        {
            var v = GetValue<int?>(InstallModeKey);
            return v is >= 0 and <= 1 ? v.Value : 0;
        }
        set => SetValue(InstallModeKey, value);
    }

    /// <summary>
    /// When true, after a successful font install the success dialog is shown briefly
    /// and the application exits automatically. When false, the user dismisses the dialog manually.
    /// </summary>
    public bool ExitAppAfterSuccessfulInstall
    {
        get => GetValue<bool?>(ExitAppAfterSuccessfulInstallKey) ?? false;
        set => SetValue(ExitAppAfterSuccessfulInstallKey, value);
    }

    /// <summary>
    /// When true, Fontager restarts with administrator privileges (UAC) on launch and when enabled in Settings.
    /// </summary>
    public bool RunAsAdministrator
    {
        get => GetValue<bool?>(RunAsAdministratorKey) ?? false;
        set => SetValue(RunAsAdministratorKey, value);
    }

    /// <summary>
    /// When true, installing for all users from a normal (non-elevated) process shows UAC once
    /// for that install only. Does not elevate the whole application.
    /// </summary>
    public bool ElevateForAllUsersInstall
    {
        get => GetValue<bool?>(ElevateForAllUsersInstallKey) ?? true;
        set => SetValue(ElevateForAllUsersInstallKey, value);
    }

    public int? WindowWidth
    {
        get => GetValue<int?>(WindowWidthKey);
        set => SetValue(WindowWidthKey, value);
    }

    public int? WindowHeight
    {
        get => GetValue<int?>(WindowHeightKey);
        set => SetValue(WindowHeightKey, value);
    }

    public int? WindowX
    {
        get => GetValue<int?>(WindowXKey);
        set => SetValue(WindowXKey, value);
    }

    public int? WindowY
    {
        get => GetValue<int?>(WindowYKey);
        set => SetValue(WindowYKey, value);
    }

    public bool WindowMaximized
    {
        get => GetValue<bool?>(WindowMaximizedKey) ?? false;
        set => SetValue(WindowMaximizedKey, value);
    }

    /// <summary>
    /// Comma-separated waterfall sizes (e.g. "8,12,16,24,32,48,72").
    /// </summary>
    public string WaterfallSizesRaw
    {
        get
        {
            var s = GetValue<string>(WaterfallSizesKey);
            return string.IsNullOrWhiteSpace(s) ? DefaultWaterfallSizesValue : s;
        }
        set => SetValue(WaterfallSizesKey, value ?? string.Empty);
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
