using System;
using System.IO;
using Fontager.Viewer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace Fontager.Viewer.Views;

/// <summary>
/// Full-window Settings page. Replaces the legacy <c>ContentDialog</c>.
///
/// Architecture:
/// <list type="bullet">
///   <item>
///     <description>Each control's change handler writes through to
///       <see cref="SettingsService"/> immediately — there is no Save or
///       Cancel button. <see cref="SettingsService"/> raises
///       <see cref="SettingsService.Changed"/> on every property write, and
///       <c>MainWindow</c> is subscribed; the user sees theme / backdrop /
///       preview changes the instant they tap the control.</description>
///   </item>
///   <item>
///     <description>Controls are populated in <see cref="OnLoaded"/> with
///       <see cref="_initialized"/> set to <c>false</c> first, so the
///       initial <c>SelectionChanged</c> / <c>Toggled</c> events that fire
///       as we set <c>SelectedIndex</c> / <c>IsOn</c> don't ricochet back
///       into <see cref="SettingsService"/>.</description>
///   </item>
///   <item>
///     <description>Returning to the viewer is handled by the back button in
///       the title bar (see <c>MainWindow.xaml</c>) — this Page is not aware
///       of how it was navigated to.</description>
///   </item>
/// </list>
/// </summary>
public sealed partial class SettingsPage : Page
{
    private readonly SettingsService _settings;
    private bool _initialized;
    private bool _isProcessElevated;
    private DispatcherQueueTimer? _previewTextDebouncer;

    public SettingsPage()
    {
        InitializeComponent();
        _settings = App.Services.GetRequiredService<SettingsService>();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Optional environment flag the host passes when navigating to the page.
    /// Currently used to enable / disable the "All users" install option.
    /// </summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is bool elevated)
            _isProcessElevated = elevated;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Appearance
        ThemeCombo.SelectedIndex = (int)_settings.Theme;
        SyncBackdropComboSelection();

        // Preview
        PreviewTextBox.Text = _settings.DefaultPreviewText;
        FontSizeSlider.Value = _settings.DefaultFontSize;
        FontSizeSliderHeaderText.Text = $"Default font size ({(int)_settings.DefaultFontSize}px)";
        PreviewControlsToggle.IsOn = _settings.ShowPreviewControls;

        // Display
        QuickViewToggle.IsOn = _settings.ShowQuickView;
        WaterfallToggle.IsOn = _settings.ShowWaterfall;
        WaterfallSizesBox.Text = _settings.WaterfallSizesRaw;

        // Install
        InstallModeCombo.SelectedIndex = _settings.InstallMode;
        ElevateForAllUsersInstallToggle.IsOn = _settings.ElevateForAllUsersInstall;
        RunAsAdminToggle.IsOn = _settings.RunAsAdministrator;
        SyncInstallAdminDescriptions();
        ExitAfterInstallToggle.IsOn = _settings.ExitAppAfterSuccessfulInstall;
        SyncInstallModeComboEnabled();

        // File association
        bool fontAssocPackaged = FileAssociationService.IsRunningPackaged;
        FontAssocToggle.IsOn = !fontAssocPackaged && FileAssociationService.IsRegistered();
        FontAssocToggle.IsEnabled = !fontAssocPackaged;
        FontAssocDescription.Text = fontAssocPackaged
            ? "Adds Fontager to the Windows 'Open with...' menu for .ttf, .otf, .ttc, and .woff2 files. Disabled while running packaged (MSIX) because the registry writes get virtualised into the package container."
            : "Adds Fontager to the Windows 'Open with...' menu for .ttf, .otf, .ttc, and .woff2 files for the current user only. Does not change the default handler.";

        // About
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
                var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                version = asm?.ToString() ?? "0.0.0.0";
            }
        }
        else
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            version = asm?.ToString() ?? "0.0.0.0";
        }

        AboutVersionText.Text = $"Version {version}";
        ApplyAboutLogo();
        // AboutBuildKindText.Text = FileAssociationService.IsRunningPackaged
        //     ? "Packaged (MSIX) build — Windows manages identity and updates."
        //     : "📦 Desktop app — wrap the published folder with an installer for Start menu & uninstall (recommended). Settings: %LocalAppData%\\Fontager\\settings.json";

        _initialized = true;

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low,
            () => ApplyTwoPaneLayout(TwoPaneRoot.ActualWidth > 1 ? TwoPaneRoot.ActualWidth : ActualWidth));
    }

    /// <summary>
    /// WinUI often fails to resolve <c>Assets/Logo.png</c> from XAML on unpackaged runs;
    /// loading from <see cref="AppContext.BaseDirectory"/> matches how files land next to the exe.
    /// </summary>
    private void ApplyAboutLogo()
    {
        try
        {
            string diskPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Logo.png");
            if (File.Exists(diskPath))
            {
                AboutLogoImage.Source = new BitmapImage
                {
                    UriSource = FileUriFromLocalPath(diskPath)
                };
                return;
            }

            AboutLogoImage.Source = new BitmapImage(new Uri("ms-appx:///Assets/Logo.png"));
        }
        catch
        {
            AboutLogoImage.Source = null;
        }
    }

    private static Uri FileUriFromLocalPath(string path)
    {
        path = Path.GetFullPath(path);
        return new Uri("file:///" + path.Replace("\\", "/", StringComparison.Ordinal));
    }

    /// <summary>Selects backdrop row by persisted <see cref="SettingsService.Backdrop"/> tag (not list index).</summary>
    private void SyncBackdropComboSelection()
    {
        int saved = _settings.Backdrop;
        for (int i = 0; i < BackdropCombo.Items.Count; i++)
        {
            if (BackdropCombo.Items[i] is ComboBoxItem cbi
                && TryReadComboBoxIntTag(cbi, out var tag)
                && tag == saved)
            {
                BackdropCombo.SelectedIndex = i;
                return;
            }
        }

        BackdropCombo.SelectedIndex = 0;
    }

    private void TwoPaneRoot_SizeChanged(object _, SizeChangedEventArgs e)
    {
        ApplyTwoPaneLayout(e.NewSize.Width);
    }

    /// <summary>
    /// Wide: settings column + gap + fixed About card (Windows 11 Settings-style).
    /// Narrow: single column; About follows settings.
    /// </summary>
    private void ApplyTwoPaneLayout(double width)
    {
        const double wideBreakpoint = 920;
        bool wide = width >= wideBreakpoint;

        if (wide)
        {
            SettingsColumnDef.Width = new GridLength(2, GridUnitType.Star);
            SettingsColumnDef.MinWidth = 280;
            GapColumnDef.Width = new GridLength(32);
            AboutColumnDef.Width = new GridLength(1, GridUnitType.Star);
            AboutColumnDef.MinWidth = 240;
            Grid.SetRow(SettingsSectionsPanel, 0);
            Grid.SetColumn(SettingsSectionsPanel, 0);
            Grid.SetColumnSpan(SettingsSectionsPanel, 1);
            Grid.SetRow(AboutCard, 0);
            Grid.SetColumn(AboutCard, 2);
            Grid.SetColumnSpan(AboutCard, 1);
            AboutCard.Margin = new Thickness(0);
            AboutCard.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else
        {
            SettingsColumnDef.Width = new GridLength(1, GridUnitType.Star);
            SettingsColumnDef.MinWidth = 0;
            GapColumnDef.Width = new GridLength(0);
            AboutColumnDef.Width = new GridLength(0);
            AboutColumnDef.MinWidth = 0;
            Grid.SetRow(SettingsSectionsPanel, 0);
            Grid.SetColumn(SettingsSectionsPanel, 0);
            Grid.SetColumnSpan(SettingsSectionsPanel, 3);
            Grid.SetRow(AboutCard, 1);
            Grid.SetColumn(AboutCard, 0);
            Grid.SetColumnSpan(AboutCard, 3);
            AboutCard.Margin = new Thickness(0, 24, 0, 0);
            AboutCard.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
    }

    /// <summary>
    /// WinRT XAML often boxes <see cref="ComboBoxItem.Tag"/> as an <see cref="int"/>
    /// when the markup uses <c>Tag="0"</c>, so <c>Tag as string</c> is null.
    /// </summary>
    private static bool TryReadComboBoxIntTag(ComboBoxItem item, out int value)
    {
        value = 0;
        switch (item.Tag)
        {
            case int i:
                value = i;
                return true;
            case long l:
                checked
                {
                    value = (int)l;
                }
                return true;
            case string s:
                return int.TryParse(s, out value);
            default:
                return item.Tag != null && int.TryParse(item.Tag.ToString(), out value);
        }
    }

    // ── Appearance ────────────────────────────────────────────────

    private void ThemeCombo_SelectionChanged(object _, SelectionChangedEventArgs _1)
    {
        if (!_initialized) return;
        if (ThemeCombo.SelectedItem is ComboBoxItem item
            && TryReadComboBoxIntTag(item, out var v)
            && v is >= 0 and <= 2)
        {
            _settings.Theme = (ElementTheme)v;
        }
    }

    private void BackdropCombo_SelectionChanged(object _, SelectionChangedEventArgs _1)
    {
        if (!_initialized) return;
        if (BackdropCombo.SelectedItem is ComboBoxItem item
            && TryReadComboBoxIntTag(item, out var v)
            && v is >= 0 and <= 3)
        {
            _settings.Backdrop = v;
        }
    }

    // ── Preview ───────────────────────────────────────────────────

    private void PreviewTextBox_TextChanged(object _, TextChangedEventArgs _1)
    {
        if (!_initialized) return;

        if (_previewTextDebouncer is null)
        {
            _previewTextDebouncer = DispatcherQueue.CreateTimer();
            _previewTextDebouncer.IsRepeating = false;
            _previewTextDebouncer.Interval = TimeSpan.FromSeconds(1);
            _previewTextDebouncer.Tick += (_, _) =>
            {
                _previewTextDebouncer!.Stop();
                _settings.DefaultPreviewText = PreviewTextBox.Text;
            };
        }

        _previewTextDebouncer.Stop();
        _previewTextDebouncer.Start();
    }

    private void PreviewTextBox_LostFocus(object _, RoutedEventArgs _1)
    {
        if (!_initialized) return;
        _previewTextDebouncer?.Stop();
        _settings.DefaultPreviewText = PreviewTextBox.Text;
    }

    private void FontSizeSlider_ValueChanged(object _, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        FontSizeSliderHeaderText.Text = $"Default font size ({(int)e.NewValue}px)";
        if (!_initialized) return;
        _settings.DefaultFontSize = e.NewValue;
    }

    private void PreviewControlsToggle_Toggled(object _, RoutedEventArgs _1)
    {
        if (!_initialized) return;
        _settings.ShowPreviewControls = PreviewControlsToggle.IsOn;
    }

    // ── Display ───────────────────────────────────────────────────

    private void QuickViewToggle_Toggled(object _, RoutedEventArgs _1)
    {
        if (!_initialized) return;
        _settings.ShowQuickView = QuickViewToggle.IsOn;
    }

    private void WaterfallToggle_Toggled(object _, RoutedEventArgs _1)
    {
        if (!_initialized) return;
        _settings.ShowWaterfall = WaterfallToggle.IsOn;
    }

    private void WaterfallSizesBox_LostFocus(object _, RoutedEventArgs _1)
    {
        if (!_initialized) return;
        _settings.WaterfallSizesRaw = WaterfallSizesBox.Text;
    }

    // ── Install ───────────────────────────────────────────────────

    private bool CanSelectAllUsersInstallTarget =>
        _isProcessElevated || _settings.ElevateForAllUsersInstall;

    private void SyncInstallModeComboEnabled()
    {
        if (InstallModeCombo.Items.Count > 1
            && InstallModeCombo.Items[1] is ComboBoxItem allUsersOption)
        {
            allUsersOption.IsEnabled = CanSelectAllUsersInstallTarget;
        }
    }

    private void SyncInstallAdminDescriptions()
    {
        InstallTargetDescription.Text = CanSelectAllUsersInstallTarget
            ? "Default target for the Install button. All users copies the font to C:\\Windows\\Fonts."
            : "Only current-user install is available. Turn on “UAC for all-users install” below, or “Run entire app as administrator”.";

        if (_isProcessElevated)
        {
            ElevateForAllUsersInstallDescription.Text =
                "The app is already running as administrator, so every install uses elevated rights. You can turn this off; installs to C:\\Windows\\Fonts still work.";
            RunAsAdminDescription.Text =
                "The entire application is elevated. Turning this off restarts without administrator rights (drag-and-drop from File Explorer works better).";
            return;
        }

        ElevateForAllUsersInstallDescription.Text = _settings.ElevateForAllUsersInstall
            ? "Recommended. Fontager stays normal while you preview fonts. Windows may show UAC only when you install to C:\\Windows\\Fonts for all users."
            : "When off, all-users install is disabled unless you use “Run entire app as administrator” below.";

        RunAsAdminDescription.Text = _settings.RunAsAdministrator
            ? "The whole app will restart elevated (UAC). Use this if you always want administrator rights — for example as the default font handler with elevated access. Each new launch may prompt UAC again."
            : "Restarts the entire app with administrator privileges. Differs from the option above: this elevates everything, not just one install to C:\\Windows\\Fonts.";
    }

    private void ElevateForAllUsersInstallToggle_Toggled(object _, RoutedEventArgs _1)
    {
        if (!_initialized) return;

        _settings.ElevateForAllUsersInstall = ElevateForAllUsersInstallToggle.IsOn;
        SyncInstallAdminDescriptions();
        SyncInstallModeComboEnabled();

        if (!CanSelectAllUsersInstallTarget && _settings.InstallMode == 1)
        {
            _initialized = false;
            InstallModeCombo.SelectedIndex = 0;
            _settings.InstallMode = 0;
            _initialized = true;
        }
    }

    private async void RunAsAdminToggle_Toggled(object _, RoutedEventArgs _1)
    {
        if (!_initialized) return;

        var wantAdmin = RunAsAdminToggle.IsOn;
        var previous = _settings.RunAsAdministrator;
        if (wantAdmin == previous)
            return;

        var xamlRoot = XamlRoot ?? (Content as FrameworkElement)?.XamlRoot;
        if (xamlRoot is null)
            return;

        var message = wantAdmin
            ? "Fontager will close and restart with administrator privileges. Windows may ask you to confirm (UAC)."
            : "Fontager will close and restart without administrator privileges. Drag-and-drop from File Explorer will work more reliably.";

        var dialog = new ContentDialog
        {
            Title = "Restart required",
            Content = message,
            PrimaryButtonText = "Restart now",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            _initialized = false;
            RunAsAdminToggle.IsOn = previous;
            _initialized = true;
            return;
        }

        _settings.RunAsAdministrator = wantAdmin;
        SyncInstallAdminDescriptions();

        if (wantAdmin == _isProcessElevated)
            return;

        ProcessElevationHelper.RestartWithElevation(wantAdmin);
    }

    private void ExitAfterInstallToggle_Toggled(object _, RoutedEventArgs _1)
    {
        if (!_initialized) return;
        _settings.ExitAppAfterSuccessfulInstall = ExitAfterInstallToggle.IsOn;
    }

    private void InstallModeCombo_SelectionChanged(object _, SelectionChangedEventArgs _1)
    {
        if (!_initialized) return;
        if (!CanSelectAllUsersInstallTarget)
        {
            return;
        }
        if (InstallModeCombo.SelectedItem is ComboBoxItem item
            && TryReadComboBoxIntTag(item, out var v)
            && v is >= 0 and <= 1)
        {
            _settings.InstallMode = v;
        }
    }

    private void FontAssocToggle_Toggled(object _, RoutedEventArgs _1)
    {
        if (!_initialized) return;
        if (FileAssociationService.IsRunningPackaged) return;

        if (FontAssocToggle.IsOn)
            FileAssociationService.RegisterForCurrentUser();
        else
            FileAssociationService.UnregisterForCurrentUser();
    }

    // ── Reset ─────────────────────────────────────────────────────

    private async void ResetButton_Click(object _, RoutedEventArgs _1)
    {
        var xamlRoot = this.XamlRoot ?? (Content as FrameworkElement)?.XamlRoot;
        if (xamlRoot is null)
            return;

        var dialog = new ContentDialog
        {
            Title = "Reset settings",
            Content = "Reset all settings to their defaults? This cannot be undone.",
            PrimaryButtonText = "Reset",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        _initialized = false;
        try
        {
            _settings.ResetToDefaults();
            OnLoaded(this, new RoutedEventArgs()); // re-populate from defaults
        }
        finally
        {
            _initialized = true;
        }
    }
}
