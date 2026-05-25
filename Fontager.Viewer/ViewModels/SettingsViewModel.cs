using System;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Fontager.Core.Helpers;
using Fontager.Core.Services;
using Fontager.Viewer.Services;
using Microsoft.UI.Xaml;

namespace Fontager.Viewer.ViewModels;

/// <summary>
/// ViewModel wrapping the SettingsService, enabling clean two-way data binding in SettingsPage.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly UpdateCheckService _updateService;

    public SettingsViewModel(SettingsService settings, UpdateCheckService updateService)
    {
        _settings = settings;
        _updateService = updateService;

        // Sync local properties if settings are changed externally
        _settings.Changed += (s, e) =>
        {
            OnPropertyChanged(string.Empty); // Notify all properties changed
        };
    }

    public ElementTheme Theme
    {
        get => _settings.Theme;
        set
        {
            if (_settings.Theme != value)
            {
                _settings.Theme = value;
                OnPropertyChanged();
            }
        }
    }

    public int Backdrop
    {
        get => _settings.Backdrop;
        set
        {
            if (_settings.Backdrop != value)
            {
                _settings.Backdrop = value;
                OnPropertyChanged();
            }
        }
    }

    public string DefaultPreviewText
    {
        get => _settings.DefaultPreviewText;
        set
        {
            if (_settings.DefaultPreviewText != value)
            {
                _settings.DefaultPreviewText = value;
                OnPropertyChanged();
            }
        }
    }

    public double DefaultFontSize
    {
        get => _settings.DefaultFontSize;
        set
        {
            if (Math.Abs(_settings.DefaultFontSize - value) > 0.001)
            {
                _settings.DefaultFontSize = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DefaultFontSizeLabel));
            }
        }
    }

    public string DefaultFontSizeLabel => $"Default font size ({(int)DefaultFontSize}px)";

    public bool ShowWaterfall
    {
        get => _settings.ShowWaterfall;
        set
        {
            if (_settings.ShowWaterfall != value)
            {
                _settings.ShowWaterfall = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ShowQuickView
    {
        get => _settings.ShowQuickView;
        set
        {
            if (_settings.ShowQuickView != value)
            {
                _settings.ShowQuickView = value;
                OnPropertyChanged();
            }
        }
    }

    public double QuickViewFontSize
    {
        get => _settings.QuickViewFontSize;
        set
        {
            if (Math.Abs(_settings.QuickViewFontSize - value) > 0.001)
            {
                _settings.QuickViewFontSize = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(QuickViewFontSizeLabel));
            }
        }
    }

    public string QuickViewFontSizeLabel => $"Quick view font size ({(int)QuickViewFontSize}px)";

    public bool ShowPreviewControls
    {
        get => _settings.ShowPreviewControls;
        set
        {
            if (_settings.ShowPreviewControls != value)
            {
                _settings.ShowPreviewControls = value;
                OnPropertyChanged();
            }
        }
    }

    public int InstallMode
    {
        get => _settings.InstallMode;
        set
        {
            if (_settings.InstallMode != value)
            {
                _settings.InstallMode = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ExitAppAfterSuccessfulInstall
    {
        get => _settings.ExitAppAfterSuccessfulInstall;
        set
        {
            if (_settings.ExitAppAfterSuccessfulInstall != value)
            {
                _settings.ExitAppAfterSuccessfulInstall = value;
                OnPropertyChanged();
            }
        }
    }

    public bool RunAsAdministrator
    {
        get => _settings.RunAsAdministrator;
        set
        {
            if (_settings.RunAsAdministrator != value)
            {
                _settings.RunAsAdministrator = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ElevateForAllUsersInstall
    {
        get => _settings.ElevateForAllUsersInstall;
        set
        {
            if (_settings.ElevateForAllUsersInstall != value)
            {
                _settings.ElevateForAllUsersInstall = value;
                OnPropertyChanged();
            }
        }
    }

    public string WaterfallSizesRaw
    {
        get => _settings.WaterfallSizesRaw;
        set
        {
            if (_settings.WaterfallSizesRaw != value)
            {
                _settings.WaterfallSizesRaw = value;
                OnPropertyChanged();
            }
        }
    }

    public int PreviewBackground
    {
        get => _settings.PreviewBackground;
        set
        {
            if (_settings.PreviewBackground != value)
            {
                _settings.PreviewBackground = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsUpdateNotificationEnabled
    {
        get => _settings.IsUpdateNotificationEnabled;
        set
        {
            if (_settings.IsUpdateNotificationEnabled != value)
            {
                _settings.IsUpdateNotificationEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime LastUpdateCheckTime => _settings.LastUpdateCheckTime;

    public string LatestAvailableVersion => _settings.LatestAvailableVersion;

    public string LatestReleaseUrl => _settings.LatestReleaseUrl;

    public int ThemeComboIndex
    {
        get => (int)Theme;
        set
        {
            if ((int)Theme != value && value is >= 0 and <= 2)
            {
                Theme = (ElementTheme)value;
                OnPropertyChanged();
            }
        }
    }

    // Combo order: Mica (0), Mica Alt (3), Acrylic (1), Solid (2) — indices differ from stored Backdrop values.
    private static readonly int[] BackdropValuesByComboIndex = [0, 3, 1, 2];

    public int BackdropComboIndex
    {
        get
        {
            int saved = Backdrop;
            for (int i = 0; i < BackdropValuesByComboIndex.Length; i++)
            {
                if (BackdropValuesByComboIndex[i] == saved)
                    return i;
            }
            return 0;
        }
        set
        {
            if (value < 0 || value >= BackdropValuesByComboIndex.Length)
                return;

            int backdropValue = BackdropValuesByComboIndex[value];
            if (Backdrop != backdropValue)
            {
                Backdrop = backdropValue;
                OnPropertyChanged();
            }
        }
    }

    // ── Install target states & dynamic descriptions ─────────────────

    public bool IsProcessElevated => ProcessElevationHelper.IsRunningElevated();

    public bool CanSelectAllUsersInstallTarget => IsProcessElevated || ElevateForAllUsersInstall;

    public string InstallTargetDescription => CanSelectAllUsersInstallTarget
        ? "Default target for the Install button. All users copies the font to C:\\Windows\\Fonts."
        : "Only current-user install is available. Turn on “UAC for all-users install” below, or “Run entire app as administrator”.";

    public string ElevateForAllUsersInstallDescription => IsProcessElevated
        ? "The app is already running as administrator, so every install uses elevated rights. You can turn this off; installs to C:\\Windows\\Fonts still work."
        : ElevateForAllUsersInstall
            ? "Recommended. Fontager stays normal while you preview fonts. Windows may show UAC only when you install to C:\\Windows\\Fonts for all users."
            : "When off, all-users install is disabled unless you use “Run entire app as administrator” below.";

    public string RunAsAdminDescription => IsProcessElevated
        ? "The entire application is elevated. Turning this off restarts without administrator rights (drag-and-drop from File Explorer works better)."
        : RunAsAdministrator
            ? "The whole app will restart elevated (UAC). Use this if you always want administrator rights — for example as the default font handler with elevated access. Each new launch may prompt UAC again."
            : "Restarts the entire app with administrator privileges. Differs from the option above: this elevates everything, not just one install to C:\\Windows\\Fonts.";

    // ── File association registration ────────────────────────────────

    public bool IsFileAssociationSupported => !FileAssociationService.IsRunningPackaged;

    public bool FontAssociationRegistered
    {
        get => IsFileAssociationSupported && FileAssociationService.IsRegistered();
        set
        {
            if (!IsFileAssociationSupported) return;

            if (value)
                FileAssociationService.RegisterForCurrentUser();
            else
                FileAssociationService.UnregisterForCurrentUser();

            OnPropertyChanged();
        }
    }

    public string FileAssociationDescriptionText => !IsFileAssociationSupported
        ? "Adds Fontager to the Windows 'Open with...' menu for .ttf, .otf, .ttc, and .woff2 files. Disabled while running packaged (MSIX) because the registry writes get virtualised into the package container."
        : "Adds Fontager to the Windows 'Open with...' menu for .ttf, .otf, .ttc, and .woff2 files for the current user only. Does not change the default handler. (Note: If upgrading from an older version, toggle this setting off and back on to restore standard right-click 'Install' options.)";

    // ── Version & Update checks ──────────────────────────────────────

    public string CurrentVersionText
    {
        get
        {
            string version;
            if (FileAssociationService.IsRunningPackaged)
            {
                try
                {
                    var ver = Windows.ApplicationModel.Package.Current.Id.Version;
                    version = $"{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}";
                }
                catch
                {
                    version = AssemblyVersionFallback();
                }
            }
            else
            {
                version = AssemblyVersionFallback();
            }
            return $"Version {version}";
        }
    }

    private static string AssemblyVersionFallback()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return asm != null ? $"{asm.Major}.{asm.Minor}.{asm.Build}" : "0.0.0";
    }

    private static string GetCurrentVersionString()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return asm != null ? $"{asm.Major}.{asm.Minor}.{asm.Build}" : "0.0.0";
    }

    public string LastUpdateCheckText
    {
        get
        {
            var dt = _settings.LastUpdateCheckTime;
            if (dt == DateTime.MinValue)
                return "Never";
            return $"{dt.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        }
    }

    public bool IsUpdateAvailable
    {
        get
        {
            var latest = _settings.LatestAvailableVersion;
            var currentVersionStr = GetCurrentVersionString();

            return !string.IsNullOrEmpty(latest) && 
                   Version.TryParse(latest, out var latestVer) && 
                   Version.TryParse(currentVersionStr, out var currentVer) && 
                   latestVer > currentVer;
        }
    }

    public void NotifyUpdatePropertiesChanged()
    {
        OnPropertyChanged(nameof(LastUpdateCheckTime));
        OnPropertyChanged(nameof(LastUpdateCheckText));
        OnPropertyChanged(nameof(LatestAvailableVersion));
        OnPropertyChanged(nameof(IsUpdateAvailable));
    }

    public void ResetToDefaults()
    {
        _settings.ResetToDefaults();
        OnPropertyChanged(string.Empty); // refresh all bindings
    }
}
