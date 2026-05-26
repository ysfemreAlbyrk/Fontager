using System;
using System.IO;
using System.Threading.Tasks;
using Fontager.Core.Helpers;
using Fontager.Core.Models;
using Fontager.Core.Services;
using Fontager.Viewer.Helpers;
using Fontager.Viewer.Services;
using Fontager.Viewer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Fontager.Viewer.Views;

/// <summary>
/// Core page displaying font outline previews, variable axes, waterfall rows, glyph grids, and metadata cards.
/// </summary>
public sealed partial class FontViewerPage : Page
{
    private readonly SettingsService _settings;
    private readonly IFontService _fontService;
    private readonly IFontInstallerService _fontInstallerService;
    private bool _isProcessElevated;
    private string? _currentFilePath;
    private int _currentFontIndex;
    private int _currentFontCount = 1;
    private string? _cachedFontDiskPath;
    private string? _activeFontPath;
    public FontViewerViewModel ViewModel { get; }

    public FontFamily? LoadedFontFamily { get; private set; }

    /// <summary>Cached page instance (NavigationCacheMode) — refreshed when backdrop/theme changes on Settings.</summary>
    internal static FontViewerPage? CachedForSettingsSync { get; private set; }

    public FontViewerPage()
    {
        InitializeComponent();

        ViewModel = App.Services.GetRequiredService<FontViewerViewModel>();
        _settings = App.Services.GetRequiredService<SettingsService>();
        _fontService = App.Services.GetRequiredService<IFontService>();
        _fontInstallerService = App.Services.GetRequiredService<IFontInstallerService>();
        _isProcessElevated = ProcessElevationHelper.IsRunningElevated();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel.HasFont)
            UpdateFontDisplay();

        SyncSelectorBarSelection();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Do not deactivate the private font session here — the page stays cached under Settings.
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ViewModel.NotifySettingsDependentPropertiesChanged();
            if (!IsDisplayReady())
                return;

            // Cached under Settings — rebuilding glyphs/waterfall/metadata here freezes the app.
            if (App.MainWindowInstance is not null && !App.MainWindowInstance.IsFontViewerPageActive)
                return;

            if (ViewModel.HasFont)
                UpdateFontDisplay();
        });
    }

    /// <summary>True when the page visual tree is attached and safe to touch named controls.</summary>
    private bool IsDisplayReady() => IsLoaded && XamlRoot is not null;
    // ── Empty-state event handlers ──────────────────────────────────────────

    private void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        _ = TriggerFileOpenPickerAsync();
    }

    private async void EmptyStateGitHub_Click(object sender, RoutedEventArgs e)
    {
        await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/ysfemreAlbworx/Fontager"));
    }

    private async void EmptyStateWebsite_Click(object sender, RoutedEventArgs e)
    {
        await Windows.System.Launcher.LaunchUriAsync(new Uri("https://fontager.app"));
    }

    private async void EmptyStateChangelog_Click(object sender, RoutedEventArgs e)
    {
        await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/ysfemreAlbworx/Fontager/releases"));
    }

    private async void EmptyStateRoadmap_Click(object sender, RoutedEventArgs e)
    {
        await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/ysfemreAlbworx/Fontager/discussions"));
    }

    private bool _suppressRecentItemOpen;

    private void RecentFilesList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (_suppressRecentItemOpen)
            return;

        if (e.ClickedItem is not RecentFileItem item)
            return;

        _ = LoadFontFromPathAsync(item.Path, 0);
    }

    private void RecentFileRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RecentFileItem item })
            return;

        _suppressRecentItemOpen = true;
        ViewModel.RemoveRecentFile(item.Path);
        DispatcherQueue.TryEnqueue(() => _suppressRecentItemOpen = false);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _settings.Changed -= OnSettingsChanged;
        base.OnNavigatedFrom(e);
    }

    /// <summary>Re-applies Quick View chrome when backdrop/theme changes while this page stays cached under Settings.</summary>
    public void RefreshQuickViewChrome()
    {
        ViewModel.NotifySettingsDependentPropertiesChanged();
    }

    private void TabSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_isSyncingTab) return; // Programmatic change — don't animate

        var selectedItem = sender.SelectedItem;
        int index = sender.Items.IndexOf(selectedItem);
        if (index >= 0)
        {
            ViewModel.SelectedTabIndex = index;
            NavigateToTab(index, animate: true);
        }
    }

    private int _lastActiveTabIndex = -1;
    private bool _isSyncingTab;

    private void NavigateToTab(int index, bool animate = false)
    {
        if (TabContentFrame == null) return;

        Type? pageType = index switch
        {
            0 => typeof(PreviewTabPage),
            1 => typeof(GlyphsTabPage),
            2 => typeof(InfoTabPage),
            _ => null
        };

        if (pageType == null) return;

        // If already showing this page, no-op
        if (TabContentFrame.CurrentSourcePageType == pageType && _lastActiveTabIndex == index)
            return;

        if (!animate || _lastActiveTabIndex < 0)
        {
            // No slide: first load, programmatic sync, font change, TTC navigation
            TabContentFrame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());
            _lastActiveTabIndex = index;
            return;
        }

        // Directional slide only when user manually switches tabs
        var effect = index > _lastActiveTabIndex
            ? SlideNavigationTransitionEffect.FromRight
            : SlideNavigationTransitionEffect.FromLeft;
        TabContentFrame.Navigate(pageType, null, new SlideNavigationTransitionInfo { Effect = effect });
        _lastActiveTabIndex = index;
    }

    private void TabContentFrame_NavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new Exception("Failed to load Tab Page: " + e.Exception.Message);
    }

    private void SyncSelectorBarSelection()
    {
        if (TabSelectorBar == null) return;
        int index = ViewModel.SelectedTabIndex;
        if (index >= 0 && index < TabSelectorBar.Items.Count)
        {
            // Suppress SelectionChanged so it doesn't trigger slide animation
            _isSyncingTab = true;
            TabSelectorBar.SelectedItem = TabSelectorBar.Items[index];
            _isSyncingTab = false;

            NavigateToTab(index, animate: false);
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        CachedForSettingsSync = this;

        _settings.Changed -= OnSettingsChanged;
        _settings.Changed += OnSettingsChanged;
        ViewModel.RefreshRecentFiles();

        // Returning from Settings: keep the open font; only refresh UI chrome.
        if (e.NavigationMode == NavigationMode.Back)
        {
            ViewModel.NotifySettingsDependentPropertiesChanged();
            if (ViewModel.HasFont)
                UpdateFontDisplay();
            SyncSelectorBarSelection();
            return;
        }

        if (e.Parameter is string filePath && !string.IsNullOrEmpty(filePath))
        {
            _ = LoadFontFromPathAsync(filePath, 0);
        }
        else if (e.NavigationMode == NavigationMode.New
                 && !string.IsNullOrEmpty(App.FontFilePath)
                 && !ViewModel.HasFont)
        {
            _ = LoadFontFromPathAsync(App.FontFilePath, 0);
        }

        SyncSelectorBarSelection();
    }

    // ── Font Loading & Cache ────────────────────────────────────────────────

    public async Task LoadFontFromPathAsync(string filePath, int fontIndex)
    {
        ViewModel.HasError = false;
        ViewModel.ErrorMessage = string.Empty;

        try
        {
            await ViewModel.LoadFontAsync(filePath, fontIndex);

            if (ViewModel.HasError)
            {
                ViewModel.HasFont = false;
                ResetWindowTitle();
                return;
            }

            if (!ViewModel.HasFont || ViewModel.CurrentFont is null)
            {
                ViewModel.HasFont = false;
                ViewModel.HasError = true;
                ViewModel.ErrorMessage = "Failed to load font file.";
                ResetWindowTitle();
                return;
            }

            bool isSameFile = _currentFilePath == filePath
                && _currentFontIndex == fontIndex
                && _cachedFontDiskPath != null
                && File.Exists(_cachedFontDiskPath);

            if (!isSameFile)
            {
                DeactivateCurrentFont();
            }

            _currentFilePath = filePath;
            _currentFontIndex = ViewModel.CurrentFont.FontIndex;
            _currentFontCount = ViewModel.CurrentFont.FontCount;

            var meta = ViewModel.CurrentFont.Metadata;
            var familyName = PickDirectWriteFamilyName(meta, filePath);

            string diskPath;
            string? msAppDataRelativePath;
            string? msAppxRelativePath;

            if (isSameFile)
            {
                diskPath = _cachedFontDiskPath!;
                var relative = $"{FontCacheFolderName}/{Path.GetFileName(diskPath)}".Replace('\\', '/');
                if (FileAssociationService.IsRunningPackaged)
                {
                    msAppDataRelativePath = relative;
                    msAppxRelativePath = null;
                }
                else if (FontCacheSetup.IsUnderInstallDirectory(diskPath))
                {
                    msAppDataRelativePath = null;
                    msAppxRelativePath = relative;
                }
                else
                {
                    msAppDataRelativePath = null;
                    msAppxRelativePath = null;
                }
            }
            else
            {
                (diskPath, msAppDataRelativePath, msAppxRelativePath) = await CacheFontForXamlAsync(filePath);
                _ = AddFontResourceEx(diskPath, FR_PRIVATE, IntPtr.Zero);
                _activeFontPath = diskPath;
            }

            LoadedFontFamily = CreateLoadedFontFamily(familyName, diskPath, msAppDataRelativePath, msAppxRelativePath);
            ViewModel.LoadedFontFamily = LoadedFontFamily;

            UpdateFontDisplay();
        }
        catch (Exception ex)
        {
            ViewModel.HasFont = false;
            ViewModel.HasError = true;
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
            ResetWindowTitle();
        }
    }

    private static void ResetWindowTitle()
    {
        if (App.MainWindowInstance != null)
            App.MainWindowInstance.AppWindow.Title = "Fontager";
    }

    private static string PickDirectWriteFamilyName(FontMetadata meta, string sourcePath)
    {
        foreach (var candidate in new[]
                 {
                     meta.TypographicFamilyName,
                     meta.FamilyName,
                     meta.PostScriptName
                 })
        {
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate.Trim();
        }

        return Path.GetFileNameWithoutExtension(sourcePath);
    }

    private static FontFamily CreateLoadedFontFamily(
        string familyName,
        string diskPath,
        string? msAppDataRelativePath,
        string? msAppxRelativePath)
    {
        var family = familyName.Trim();
        if (!string.IsNullOrEmpty(msAppxRelativePath))
        {
            var rel = msAppxRelativePath.Replace('\\', '/');
            return new FontFamily($"ms-appx:///{rel}#{family}");
        }

        if (!string.IsNullOrEmpty(msAppDataRelativePath))
        {
            var rel = msAppDataRelativePath.Replace('\\', '/');
            return new FontFamily($"ms-appdata:///local/{rel}#{family}");
        }

        return new FontFamily(family);
    }

    private async Task<(string DiskPath, string? MsAppDataRelativePath, string? MsAppxRelativePath)> CacheFontForXamlAsync(string sourceFilePath)
    {
        bool useMsAppData = false;
        string cacheDir;

        if (FileAssociationService.IsRunningPackaged)
        {
            try
            {
                var localFolder = ApplicationData.Current.LocalFolder;
                var cacheFolder = await localFolder.CreateFolderAsync(FontCacheFolderName, CreationCollisionOption.OpenIfExists);
                cacheDir = cacheFolder.Path;
                useMsAppData = true;
            }
            catch
            {
                cacheDir = FontCacheSetup.EnsureWritableCacheDirectory();
                useMsAppData = false;
            }
        }
        else
        {
            cacheDir = FontCacheSetup.EnsureWritableCacheDirectory();
            useMsAppData = false;
        }

        if (_cachedFontDiskPath is not null)
        {
            try
            {
                if (File.Exists(_cachedFontDiskPath))
                    File.Delete(_cachedFontDiskPath);
            }
            catch { /* ignore */ }
        }

        var destPath = await Task.Run(() =>
        {
            var rawBytes = File.ReadAllBytes(sourceFilePath);

            if (Woff2Decoder.IsWoff2(rawBytes))
            {
                var sfntBytes = Woff2Decoder.DecodeToSfnt(rawBytes);
                var newExt = Woff2Decoder.IsOpenTypeFlavor(sfntBytes) ? ".otf" : ".ttf";
                var name = $"{Guid.NewGuid():N}{newExt}";
                var path = Path.Combine(cacheDir, name);
                File.WriteAllBytes(path, sfntBytes);
                return path;
            }

            var ext = Path.GetExtension(sourceFilePath);
            var uniqueName = $"{Guid.NewGuid():N}{ext}";
            var path2 = Path.Combine(cacheDir, uniqueName);
            File.Copy(sourceFilePath, path2, overwrite: true);
            return path2;
        });

        _cachedFontDiskPath = destPath;

        var relative = $"{FontCacheFolderName}/{Path.GetFileName(destPath)}".Replace('\\', '/');

        if (useMsAppData)
            return (destPath, relative, null);

        if (FontCacheSetup.IsUnderInstallDirectory(destPath))
            return (destPath, null, relative);

        return (destPath, null, null);
    }

    private void DeactivateCurrentFont()
    {
        if (_activeFontPath != null)
        {
            RemoveFontResourceEx(_activeFontPath, FR_PRIVATE, IntPtr.Zero);
            _activeFontPath = null;
        }
    }

    // ── UI Updates ──────────────────────────────────────────────────────────

    private void UpdateFontDisplay()
    {
        var font = ViewModel.CurrentFont;
        if (font is null || !IsDisplayReady())
            return;

        ViewModel.NotifySettingsDependentPropertiesChanged();

        if (App.MainWindowInstance?.AppWindow is { } appWindow)
            appWindow.Title = $"Fontager \u2014 {font.DisplayName}";

        // Update installation button
        UpdateInstallButtonPresentation(GetSavedInstallTarget());
        ApplyInstallElevatedUi();

        // Build Quick View
        BuildQuickView();

        SyncSelectorBarSelection();
    }

    private void ApplyFontToTextBlock(TextBlock tb, FontMetadata meta)
    {
        if (LoadedFontFamily != null)
        {
            tb.FontFamily = LoadedFontFamily;
        }

        tb.FontWeight = new Windows.UI.Text.FontWeight((ushort)meta.Weight);
        tb.FontStyle = meta.IsItalic ? Windows.UI.Text.FontStyle.Italic
            : meta.IsOblique ? Windows.UI.Text.FontStyle.Oblique
            : Windows.UI.Text.FontStyle.Normal;
    }

    // ── Quick View ──────────────────────────────────────────────────────────

    private void BuildQuickView()
    {
        QuickViewPanel.Children.Clear();

        var meta = ViewModel.CurrentFont?.Metadata;
        if (meta == null) return;

        string[] lines =
        [
            "abcdefghijklmnopqrstuvwxyz ABCDEFGHIJKLMNOPQRSTUVWXYZ",
            "1234567890.:,;'\"(!?) +-*/="
        ];

        foreach (var line in lines)
        {
            var tb = new TextBlock
            {
                Text = line,
                FontSize = _settings.QuickViewFontSize,
                TextWrapping = TextWrapping.WrapWholeWords,
                IsTextSelectionEnabled = true,
                Margin = new Thickness(0, 1, 0, 1)
            };
            ApplyFontToTextBlock(tb, meta);
            QuickViewPanel.Children.Add(tb);
        }
    }



    private bool IsApplicationLightTheme() =>
        AppThemeHelper.IsLightTheme(_settings.Theme, this);

    // ── Preview Interaction Handlers ────────────────────────────────────────



    private void PrevFontButton_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadFontFromPathAsync(_currentFilePath!, _currentFontIndex - 1);
    }

    private void NextFontButton_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadFontFromPathAsync(_currentFilePath!, _currentFontIndex + 1);
    }

    // ── File Pickers & Triggers ──────────────────────────────────────────────

    public async Task TriggerFileOpenPickerAsync()
    {
        string? path = await PickFontFilePathAsync();
        if (!string.IsNullOrEmpty(path))
        {
            await LoadFontFromPathAsync(path, 0);
        }
    }

    private async Task<string?> PickFontFilePathAsync()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        string[] extensions = [".ttf", ".otf", ".ttc", ".woff2"];

        if (!ProcessElevationHelper.IsRunningElevated())
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker
                {
                    ViewMode = Windows.Storage.Pickers.PickerViewMode.List,
                    SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary
                };
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                foreach (var ext in extensions)
                    picker.FileTypeFilter.Add(ext);

                var file = await picker.PickSingleFileAsync();
                return file?.Path;
            }
            catch
            {
                // Fall through to Win32 path
            }
        }

        return await Task.Run(() =>
            Win32FileDialog.PickSingleFile(hwnd, "Open font file", "Font files", extensions));
    }

    // ── Metadata Building ───────────────────────────────────────────────────



    private static string FormatMetric(int value) => value == 0 ? string.Empty : value.ToString();

    private static string GetWidthName(int width) => width switch
    {
        1 => "Ultra-condensed (1)",
        2 => "Extra-condensed (2)",
        3 => "Condensed (3)",
        4 => "Semi-condensed (4)",
        5 => "Normal (5)",
        6 => "Semi-expanded (6)",
        7 => "Expanded (7)",
        8 => "Extra-expanded (8)",
        9 => "Ultra-expanded (9)",
        _ => $"Width ({width})"
    };

    private static string GetWeightName(int weight) => weight switch
    {
        100 => "Thin (100)",
        200 => "Extra-light (200)",
        300 => "Light (300)",
        400 => "Regular (400)",
        500 => "Medium (500)",
        600 => "Semi-bold (600)",
        700 => "Bold (700)",
        800 => "Extra-bold (800)",
        900 => "Black (900)",
        950 => "Extra-black (950)",
        _ => $"Weight ({weight})"
    };



    // ── Installation Helpers ────────────────────────────────────────────────

    private enum InstallTarget
    {
        CurrentUser = 0,
        AllUsers = 1
    }

    private Task NotifyInstallSuccessAsync(string title, string message) =>
        _settings.ExitAppAfterSuccessfulInstall
            ? ShowInstallSuccessThenExitAppAsync(title, message)
            : ShowSuccessDialogAsync(title, message);

    private async void InstallSplitButton_Click(SplitButton sender, SplitButtonClickEventArgs args)
    {
        await InstallFontAsync(GetSavedInstallTarget());
    }

    private async void InstallCurrentUserMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetSavedInstallTarget(InstallTarget.CurrentUser);
        await InstallFontAsync(InstallTarget.CurrentUser);
    }

    private async void InstallAllUsersMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetSavedInstallTarget(InstallTarget.AllUsers);
        await InstallFontAsync(InstallTarget.AllUsers);
    }

    private bool CanInstallForAllUsers => _isProcessElevated || _settings.ElevateForAllUsersInstall;

    private InstallTarget GetSavedInstallTarget()
    {
        if (!CanInstallForAllUsers)
            return InstallTarget.CurrentUser;

        return _settings.InstallMode == (int)InstallTarget.AllUsers
            ? InstallTarget.AllUsers
            : InstallTarget.CurrentUser;
    }

    private void SetSavedInstallTarget(InstallTarget target)
    {
        if (target == InstallTarget.AllUsers && !CanInstallForAllUsers)
            target = InstallTarget.CurrentUser;

        _settings.InstallMode = (int)target;
        UpdateInstallButtonPresentation(GetSavedInstallTarget());
    }

    private void UpdateInstallButtonPresentation(InstallTarget target)
    {
        bool isAllUsers = target == InstallTarget.AllUsers;
        InstallButtonText.Text = isAllUsers ? "Install (All users)" : "Install";

        string tip;
        if (_isProcessElevated)
        {
            tip = isAllUsers
                ? "Install font for all users (Windows\\Fonts, machine-wide)"
                : "Install font for the current user only";
        }
        else if (isAllUsers && _settings.ElevateForAllUsersInstall)
        {
            tip = "Install to C:\\Windows\\Fonts for all users. Windows may show UAC once for this install only.";
        }
        else
        {
            tip = isAllUsers
                ? "Install font for all users (enable “UAC for all-users install” in Settings, or run the entire app as administrator)"
                : "Install font for the current user only";
        }

        ToolTipService.SetToolTip(InstallSplitButton, tip);
    }

    private void ApplyInstallElevatedUi()
    {
        InstallAllUsersMenuFlyoutItem.IsEnabled = CanInstallForAllUsers;
    }

    private async Task InstallFontAsync(InstallTarget target)
    {
        if (_currentFilePath == null) return;

        if (ViewModel.CurrentFont?.Format == FontFormat.WebOpenFont)
        {
            await ShowInfoDialogAsync(
                "Installation not supported",
                "Windows cannot install WOFF2 fonts. Convert to TrueType (.ttf) or OpenType (.otf) to install the font.");
            return;
        }

        var fontDisplayName = ViewModel.CurrentFont?.DisplayName ?? Path.GetFileNameWithoutExtension(_currentFilePath);
        var fileName = Path.GetFileName(_currentFilePath);
        var installTarget = target == InstallTarget.AllUsers ? FontInstallTarget.AllUsers : FontInstallTarget.CurrentUser;

        var destDir = installTarget == FontInstallTarget.AllUsers 
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");
        var destPath = Path.Combine(destDir, fileName);

        var overwrite = false;
        if (File.Exists(destPath))
        {
            var targetName = installTarget == FontInstallTarget.AllUsers ? "system-wide" : "for the current user";
            var confirm = await ShowConfirmDialogAsync("Font Already Installed", $"This font is already installed {targetName}. Overwrite?");
            if (!confirm) return;
            overwrite = true;
        }

        if (installTarget == FontInstallTarget.AllUsers && !_isProcessElevated && !_settings.ElevateForAllUsersInstall)
        {
            await ShowInfoDialogAsync(
                "Administrator required",
                "Installing to C:\\Windows\\Fonts needs either “UAC for all-users install” in Settings (recommended), or “Run entire app as administrator”.");
            return;
        }

        try
        {
            var result = await _fontInstallerService.InstallFontAsync(_currentFilePath, fontDisplayName, installTarget, overwrite);

            switch (result)
            {
                case FontInstallResult.Success:
                    var successText = installTarget == FontInstallTarget.AllUsers ? "for all users" : "for the current user and is now visible in Settings → Fonts";
                    await NotifyInstallSuccessAsync("Font installed", $"'{fontDisplayName}' has been installed {successText}.");
                    break;

                case FontInstallResult.AlreadyExists:
                    var existText = installTarget == FontInstallTarget.AllUsers ? "system-wide" : "for the current user";
                    await ShowInfoDialogAsync("Font Already Installed", $"This font is already installed {existText}.");
                    break;

                case FontInstallResult.AccessDenied:
                    await ShowErrorDialogAsync("Installation failed", "Access denied. Installation requires running the application as administrator.");
                    break;

                case FontInstallResult.Failed:
                default:
                    if (installTarget == FontInstallTarget.CurrentUser && FileAssociationService.IsRunningPackaged)
                    {
                        await ShowWarningDialogAsync("Installation incomplete",
                            $"The font file was copied to:\n{destPath}\n\nbut the registry entry under HKCU could not be verified. This usually means the app is running with a virtualized registry (packaged identity). Run Fontager unpackaged or use 'Install for all users' instead.");
                    }
                    else
                    {
                        await ShowErrorDialogAsync("Installation failed", "Could not install the font. Try again or use Settings → Run entire app as administrator.");
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync("Installation failed", $"Could not install font: {ex.Message}");
        }
    }

    // ── Dialog Helpers ──────────────────────────────────────────────────────

    private async Task<bool> ShowConfirmDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "Yes",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task ShowInfoDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async Task ShowSuccessDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = BuildDialogHeroPanel("\uE73E", ResolveThemeBrush("SystemFillColorSuccessBrush", Microsoft.UI.Colors.ForestGreen), message),
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async Task ShowInstallSuccessThenExitAppAsync(string title, string message)
    {
        var dq = DispatcherQueue;
        var dialog = new ContentDialog
        {
            Title = title,
            Content = BuildDialogHeroPanel("\uE73E", ResolveThemeBrush("SystemFillColorSuccessBrush", Microsoft.UI.Colors.ForestGreen), message),
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };

        var showTask = dialog.ShowAsync().AsTask();
        await Task.Delay(TimeSpan.FromMicroseconds(300));

        if (dq.HasThreadAccess)
        {
            try { dialog.Hide(); }
            catch { /* already closed */ }
        }
        else
        {
            var hideDone = new TaskCompletionSource();
            dq.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                try { dialog.Hide(); }
                catch { /* already closed */ }
                hideDone.TrySetResult();
            });
            await hideDone.Task;
        }

        try
        {
            await showTask;
        }
        catch
        {
            // ignore
        }

        if (dq.HasThreadAccess)
            Application.Current.Exit();
        else
            dq.TryEnqueue(() => Application.Current.Exit());
    }

    private async Task ShowWarningDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = BuildDialogHeroPanel("\uE7BA", ResolveThemeBrush("SystemFillColorCautionBrush", Microsoft.UI.Colors.DarkOrange), message),
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async Task ShowErrorDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = BuildDialogHeroPanel("\uE783", ResolveThemeBrush("SystemFillColorCriticalBrush", Microsoft.UI.Colors.Firebrick), message),
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private static StackPanel BuildDialogHeroPanel(string glyph, Brush iconBrush, string message)
    {
        var panel = new StackPanel { Spacing = 16 };

        panel.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 44,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = iconBrush
        });

        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        });

        return panel;
    }

    private Brush ResolveThemeBrush(string resourceKey, Windows.UI.Color? fallback = null)
    {
        var color = resourceKey is "TextFillColorTertiaryBrush" or "TextFillColorSecondaryBrush"
            or "CardBackgroundFillColorDefaultBrush" or "CardStrokeColorDefaultBrush"
            ? AppThemeHelper.ThemeColor(resourceKey, IsApplicationLightTheme())
            : fallback ?? AppThemeHelper.ThemeColor(resourceKey, IsApplicationLightTheme());

        return new SolidColorBrush(color);
    }



    // ── Win32 private GDI helper imports ────────────────────────────────────

    [System.Runtime.InteropServices.DllImport("gdi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int AddFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

    [System.Runtime.InteropServices.DllImport("gdi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool RemoveFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

    private const uint FR_PRIVATE = 0x10;
    private const string FontCacheFolderName = "FontCache";
}
