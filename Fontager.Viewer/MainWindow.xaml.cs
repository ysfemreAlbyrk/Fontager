using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using Fontager.Core.Helpers;
using Fontager.Core.Models;
using Fontager.Core.Services;
using Fontager.Viewer.Services;
using Fontager.Viewer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Fontager.Viewer;

public sealed partial class MainWindow : Window
{
    private enum InstallTarget
    {
        CurrentUser = 0,
        AllUsers = 1
    }

    private readonly FontViewerViewModel _viewModel;
    private readonly SettingsService _settings;
    private readonly IFontService _fontService;
    private FontFamily? _loadedFontFamily;

    // ── Win32 Interop ───────────────────────────────────────────

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int AddFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool RemoveFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetDpiForWindow(IntPtr hwnd);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private WndProcDelegate? _wndProcDelegate;
    private IntPtr _oldWndProc;

    private const int GWL_WNDPROC = -4;
    private const uint WM_GETMINMAXINFO = 0x0024;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    private const uint FR_PRIVATE = 0x10;
    private const string FontCacheFolderName = "FontCache";

    private string? _activeFontPath;
    private string? _currentFilePath;
    private int _currentFontIndex;
    private int _currentFontCount = 1;
    private string? _cachedFontFileName;
    private bool _quickViewAutoShown; // true when Quick View was auto-shown due to small window

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = App.Services.GetRequiredService<FontViewerViewModel>();
        _settings = App.Services.GetRequiredService<SettingsService>();
        _fontService = App.Services.GetRequiredService<IFontService>();

        ConfigureWindow();
        ApplySettings();
        ApplyBackdrop();

        RootGrid.AllowDrop = true;
        RootGrid.DragOver += RootGrid_DragOver;
        RootGrid.Drop += RootGrid_Drop;

        SetAppVersion();

        // Auto-show/hide Quick View based on window size
        this.SizeChanged += OnWindowSizeChanged;

        if (!string.IsNullOrEmpty(App.FontFilePath))
            _ = LoadFontFromPathAsync(App.FontFilePath, 0);
    }

    private void OnWindowSizeChanged(object sender, WindowSizeChangedEventArgs args)
    {
        if (!_viewModel.HasFont) return;

        const double compactWidth = 630;
        const double compactHeight = 340;

        var bounds = args.Size;
        bool isSmall = bounds.Width < compactWidth || bounds.Height < compactHeight;

        if (isSmall && !_settings.ShowQuickView && !_quickViewAutoShown)
        {
            // Auto-show Quick View in compact mode
            _quickViewAutoShown = true;
            QuickViewSection.Visibility = Visibility.Visible;
            BuildQuickView();
        }
        else if (!isSmall && _quickViewAutoShown)
        {
            // Restore original setting when window is large again
            _quickViewAutoShown = false;
            QuickViewSection.Visibility = _settings.ShowQuickView ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ConfigureWindow()
    {
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1300, 750));
        AppWindow.Title = "Fontager";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        SetMinimumWindowSize(600, 266);

        ApplyWindowIcon();
        AllowDragDropFromLowerIntegrity();
    }

    /// <summary>
    /// When Fontager runs elevated, Windows UIPI blocks lower-integrity Explorer
    /// from delivering drag-drop and clipboard messages. Whitelist the three
    /// relevant window messages so drag-drop and paste keep working under
    /// "Run as administrator".
    /// </summary>
    private void AllowDragDropFromLowerIntegrity()
    {
        if (!IsRunningElevated()) return;

        var hwnd = WindowNative.GetWindowHandle(this);
        ChangeWindowMessageFilterEx(hwnd, WM_DROPFILES, MSGFLT_ALLOW, IntPtr.Zero);
        ChangeWindowMessageFilterEx(hwnd, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);
        ChangeWindowMessageFilterEx(hwnd, WM_COPYGLOBALDATA, MSGFLT_ALLOW, IntPtr.Zero);
    }

    private static bool IsRunningElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets the window icon used by Alt+Tab, the taskbar, and the title bar.
    /// AppWindow.SetIcon works for packaged and unpackaged builds; we also push
    /// the icon via WM_SETICON so the Alt+Tab thumbnail picks it up reliably
    /// when the package install directory differs from the working directory.
    /// </summary>
    private void ApplyWindowIcon()
    {
        var hwnd = WindowNative.GetWindowHandle(this);

        // Resolve the icon path: prefer the package install location when
        // running packaged, fall back to the executable directory.
        string iconPath = ResolveAssetPath("Assets\\Logo.ico");

        try
        {
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }
        }
        catch
        {
            // SetIcon can throw on some platform/SDK combinations; fall through
            // to the Win32 path which is what populates the Alt+Tab thumbnail.
        }

        if (!File.Exists(iconPath)) return;

        var smallIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
        var bigIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);

        if (smallIcon != IntPtr.Zero)
            SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, smallIcon);
        if (bigIcon != IntPtr.Zero)
            SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, bigIcon);
    }

    private static string ResolveAssetPath(string relativePath)
    {
        try
        {
            var packagePath = Package.Current.InstalledLocation.Path;
            var packaged = Path.Combine(packagePath, relativePath);
            if (File.Exists(packaged)) return packaged;
        }
        catch
        {
            // Not running packaged.
        }

        var baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, relativePath);
    }

    private void SetAppVersion()
    {
        string versionStr;
        try
        {
            var version = Package.Current.Id.Version;
            versionStr = $"v{version.Major}.{version.Minor}.{version.Build}";
        }
        catch
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            versionStr = asm != null ? $"v{asm.Major}.{asm.Minor}.{asm.Build}" : "v0.0.0";
        }
        TitleBarVersion.Text = versionStr;
        if (EmptyStateVersion != null)
            EmptyStateVersion.Text = versionStr;
    }

    private async void EmptyStateGitHub_Click(object sender, RoutedEventArgs e)
    {
        await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/ysfemreAlbyrk/Fontager"));
    }

    private void SetMinimumWindowSize(int minWidthDip, int minHeightDip)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        _wndProcDelegate = (hWnd, msg, wParam, lParam) =>
        {
            if (msg == WM_GETMINMAXINFO)
            {
                var dpi = GetDpiForWindow(hWnd);
                var scale = dpi / 96.0;
                var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                info.ptMinTrackSize.x = (int)(minWidthDip * scale);
                info.ptMinTrackSize.y = (int)(minHeightDip * scale);
                Marshal.StructureToPtr(info, lParam, true);
                return IntPtr.Zero;
            }
            return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
        };
        _oldWndProc = SetWindowLongPtr(hwnd, GWL_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
    }

    private void ApplyBackdrop()
    {
        SystemBackdrop = _settings.Backdrop == 1
            ? new DesktopAcrylicBackdrop()
            : new MicaBackdrop();
    }

    // ── File Open ──────────────────────────────────────────────

    private async void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        picker.ViewMode = PickerViewMode.List;
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".ttf");
        picker.FileTypeFilter.Add(".otf");
        picker.FileTypeFilter.Add(".ttc");
        picker.FileTypeFilter.Add(".woff2");

        var file = await picker.PickSingleFileAsync();
        if (file != null)
            await LoadFontFromPathAsync(file.Path, 0);
    }

    // ── Install ────────────────────────────────────────────────

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

    private InstallTarget GetSavedInstallTarget() =>
        _settings.InstallMode == (int)InstallTarget.AllUsers
            ? InstallTarget.AllUsers
            : InstallTarget.CurrentUser;

    private void SetSavedInstallTarget(InstallTarget target)
    {
        _settings.InstallMode = (int)target;
        UpdateInstallButtonPresentation(target);
    }

    private void UpdateInstallButtonPresentation(InstallTarget target)
    {
        bool isAllUsers = target == InstallTarget.AllUsers;
        InstallButtonText.Text = isAllUsers ? "Install (All users)" : "Install (Current user)";
        ToolTipService.SetToolTip(
            InstallSplitButton,
            isAllUsers ? "Install font for all users (requires admin)" : "Install font for current user");
    }

    private async Task InstallFontAsync(InstallTarget target)
    {
        if (_currentFilePath == null) return;

        bool installSystem = target == InstallTarget.AllUsers;

        try
        {
            var fontDisplayName = _viewModel.CurrentFont?.DisplayName
                ?? Path.GetFileNameWithoutExtension(_currentFilePath);
            var fileName = Path.GetFileName(_currentFilePath);

            if (installSystem)
            {
                // System-wide install: copy to C:\Windows\Fonts, register in HKLM
                var systemFontsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
                var destPath = Path.Combine(systemFontsDir, fileName);

                if (File.Exists(destPath))
                {
                    var confirm = await ShowConfirmDialogAsync("Font Already Installed",
                        "This font is already installed system-wide. Overwrite?");
                    if (!confirm) return;
                }

                File.Copy(_currentFilePath, destPath, true);

                using var regKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts", true);
                regKey?.SetValue(fontDisplayName, fileName);

                await ShowInfoDialogAsync("Font Installed", $"'{fontDisplayName}' has been installed for all users.");
            }
            else
            {
                // Per-user install: copy to LocalAppData\Microsoft\Windows\Fonts, register in HKCU
                var userFontsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Windows", "Fonts");
                Directory.CreateDirectory(userFontsDir);

                var destPath = Path.Combine(userFontsDir, fileName);

                if (File.Exists(destPath))
                {
                    var confirm = await ShowConfirmDialogAsync("Font Already Installed",
                        "This font is already installed for the current user. Overwrite?");
                    if (!confirm) return;
                }

                File.Copy(_currentFilePath, destPath, true);

                using var regKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts", true);
                regKey?.SetValue(fontDisplayName, destPath);

                await ShowInfoDialogAsync("Font Installed", $"'{fontDisplayName}' has been installed for the current user.");
            }
        }
        catch (UnauthorizedAccessException)
        {
            await ShowInfoDialogAsync("Installation Failed",
                "Access denied. System-wide installation requires running the application as administrator.");
        }
        catch (Exception ex)
        {
            await ShowInfoDialogAsync("Installation Failed", $"Could not install font: {ex.Message}");
        }
    }

    // ── Drag & Drop ────────────────────────────────────────────

    private void RootGrid_DragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Open font";
        e.DragUIOverride.IsCaptionVisible = true;
    }

    private async void RootGrid_Drop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            return;

        var items = await e.DataView.GetStorageItemsAsync();
        if (items.Count > 0 && items[0] is Windows.Storage.StorageFile file && _fontService.IsSupportedFont(file.Path))
            await LoadFontFromPathAsync(file.Path, 0);
    }

    // ── Multi-Font Navigation ──────────────────────────────────

    private async void PrevFontButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFilePath != null && _currentFontIndex > 0)
            await LoadFontFromPathAsync(_currentFilePath, _currentFontIndex - 1);
    }

    private async void NextFontButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFilePath != null && _currentFontIndex < _currentFontCount - 1)
            await LoadFontFromPathAsync(_currentFilePath, _currentFontIndex + 1);
    }

    // ── Font Loading ───────────────────────────────────────────

    private async Task LoadFontFromPathAsync(string filePath, int fontIndex)
    {
        ShowState(loading: true);

        try
        {
            await _viewModel.LoadFontAsync(filePath, fontIndex);

            if (_viewModel.HasError)
            {
                ErrorText.Text = _viewModel.ErrorMessage;
                ShowState(error: true);
                return;
            }

            if (!_viewModel.HasFont || _viewModel.CurrentFont is null)
            {
                ErrorText.Text = "Failed to load font file.";
                ShowState(error: true);
                return;
            }

            DeactivateCurrentFont();

            AddFontResourceEx(filePath, FR_PRIVATE, IntPtr.Zero);
            _activeFontPath = filePath;
            _currentFilePath = filePath;
            _currentFontIndex = _viewModel.CurrentFont.FontIndex;
            _currentFontCount = _viewModel.CurrentFont.FontCount;

            // Use TypographicFamilyName (name ID 16) for XAML FontFamily resolution.
            // This is critical for fonts like Material Icons where name ID 1 differs
            // from the canonical family name that DirectWrite expects.
            var meta = _viewModel.CurrentFont.Metadata;
            var familyName = !string.IsNullOrWhiteSpace(meta.TypographicFamilyName)
                ? meta.TypographicFamilyName
                : !string.IsNullOrWhiteSpace(meta.FamilyName)
                    ? meta.FamilyName
                    : Path.GetFileNameWithoutExtension(filePath);

            var cachedUri = await CacheFontForXamlAsync(filePath);
            _loadedFontFamily = new FontFamily($"{cachedUri}#{familyName}");

            UpdateFontDisplay();
            ShowState(content: true);
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"Error: {ex.Message}";
            ShowState(error: true);
        }
    }

    private async Task<string> CacheFontForXamlAsync(string sourceFilePath)
    {
        var localFolder = ApplicationData.Current.LocalFolder;
        var cacheFolder = await localFolder.CreateFolderAsync(
            FontCacheFolderName, CreationCollisionOption.OpenIfExists);

        // Clean previous cached font
        if (_cachedFontFileName != null)
        {
            try
            {
                var oldFile = await cacheFolder.TryGetItemAsync(_cachedFontFileName);
                if (oldFile is StorageFile sf)
                    await sf.DeleteAsync(StorageDeleteOption.PermanentDelete);
            }
            catch { /* ignore */ }
        }

        var ext = Path.GetExtension(sourceFilePath);
        var uniqueName = $"{Guid.NewGuid():N}{ext}";

        var sourceFile = await StorageFile.GetFileFromPathAsync(sourceFilePath);
        await sourceFile.CopyAsync(cacheFolder, uniqueName, NameCollisionOption.ReplaceExisting);

        _cachedFontFileName = uniqueName;
        return $"ms-appdata:///local/{FontCacheFolderName}/{uniqueName}";
    }

    private void DeactivateCurrentFont()
    {
        if (_activeFontPath != null)
        {
            RemoveFontResourceEx(_activeFontPath, FR_PRIVATE, IntPtr.Zero);
            _activeFontPath = null;
        }
    }

    // ── Display Update ─────────────────────────────────────────

    private void UpdateFontDisplay()
    {
        var font = _viewModel.CurrentFont;
        if (font is null) return;
        var meta = font.Metadata;

        // Title bar
        TitleBarFontName.Text = font.DisplayName;
        AppWindow.Title = $"Fontager \u2014 {font.DisplayName}";

        // Header
        FontFamilyName.Text = !string.IsNullOrWhiteSpace(meta.TypographicFamilyName)
            ? meta.TypographicFamilyName : meta.FamilyName;
        FontStyleName.Text = meta.SubfamilyName;
        FormatBadgeText.Text = font.Format.ToString();
        VariableBadge.Visibility = meta.IsVariable ? Visibility.Visible : Visibility.Collapsed;
        FontFileSize.Text = font.FormattedFileSize;

        // Multi-font navigation
        if (font.FontCount > 1)
        {
            FontNavPanel.Visibility = Visibility.Visible;
            FontIndexLabel.Text = $"{font.FontIndex + 1} / {font.FontCount}";
            PrevFontButton.IsEnabled = font.FontIndex > 0;
            NextFontButton.IsEnabled = font.FontIndex < font.FontCount - 1;
        }
        else
        {
            FontNavPanel.Visibility = Visibility.Collapsed;
        }

        // Apply font to preview
        ApplyFontToElement(PreviewTextBox, meta);

        // Preview section (editable area + slider)
        PreviewSection.Visibility = _settings.ShowPreviewControls ? Visibility.Visible : Visibility.Collapsed;

        // Quick View
        _quickViewAutoShown = false;
        QuickViewSection.Visibility = _settings.ShowQuickView ? Visibility.Visible : Visibility.Collapsed;
        BuildQuickView();

        // Waterfall
        WaterfallSection.Visibility = _settings.ShowWaterfall ? Visibility.Visible : Visibility.Collapsed;
        BuildWaterfallView();

        BuildGlyphGrid();
        BuildMetadataView();
    }

    /// <summary>
    /// Applies the loaded font family, weight, and style to a TextBlock or TextBox.
    /// </summary>
    private void ApplyFontToElement(Control element, FontMetadata meta)
    {
        if (_loadedFontFamily != null)
            element.FontFamily = _loadedFontFamily;

        element.FontWeight = new Windows.UI.Text.FontWeight((ushort)meta.Weight);
        element.FontStyle = meta.IsItalic ? Windows.UI.Text.FontStyle.Italic
            : meta.IsOblique ? Windows.UI.Text.FontStyle.Oblique
            : Windows.UI.Text.FontStyle.Normal;
    }

    private void ApplyFontToTextBlock(TextBlock tb, FontMetadata meta)
    {
        if (_loadedFontFamily != null)
            tb.FontFamily = _loadedFontFamily;

        tb.FontWeight = new Windows.UI.Text.FontWeight((ushort)meta.Weight);
        tb.FontStyle = meta.IsItalic ? Windows.UI.Text.FontStyle.Italic
            : meta.IsOblique ? Windows.UI.Text.FontStyle.Oblique
            : Windows.UI.Text.FontStyle.Normal;
    }

    // ── Quick View ─────────────────────────────────────────────

    private void BuildQuickView()
    {
        QuickViewPanel.Children.Clear();

        var meta = _viewModel.CurrentFont?.Metadata;
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
                FontSize = 20,
                TextWrapping = TextWrapping.WrapWholeWords,
                IsTextSelectionEnabled = true,
                Margin = new Thickness(0, 1, 0, 1)
            };
            ApplyFontToTextBlock(tb, meta);
            QuickViewPanel.Children.Add(tb);
        }
    }

    // ── Preview ────────────────────────────────────────────────

    private void PreviewTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _viewModel.PreviewText = PreviewTextBox.Text;
        if (_viewModel.HasFont)
            BuildWaterfallView();
    }

    private void FontSizeUpButton_Click(object sender, RoutedEventArgs e)
    {
        var newSize = Math.Min(PreviewTextBox.FontSize + 2, 120);
        SetPreviewFontSize(newSize);
    }

    private void FontSizeDownButton_Click(object sender, RoutedEventArgs e)
    {
        var newSize = Math.Max(PreviewTextBox.FontSize - 2, 8);
        SetPreviewFontSize(newSize);
    }

    private void SetPreviewFontSize(double size)
    {
        PreviewTextBox.FontSize = size;
        FontSizeLabel.Text = $"{(int)size}";
    }

    // ── Waterfall ──────────────────────────────────────────────

    private void BuildWaterfallView()
    {
        WaterfallPanel.Children.Clear();
        if (!_settings.ShowWaterfall) return;

        var meta = _viewModel.CurrentFont?.Metadata;
        if (meta == null) return;

        var sizes = _settings.GetWaterfallSizes();
        var text = string.IsNullOrWhiteSpace(_viewModel.PreviewText)
            ? "The quick brown fox jumps over the lazy dog"
            : _viewModel.PreviewText;

        foreach (var size in sizes)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var sizeLabel = new TextBlock
            {
                Text = $"{size}",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(sizeLabel, 0);

            var tb = new TextBlock
            {
                Text = text,
                FontSize = size,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsTextSelectionEnabled = true,
                VerticalAlignment = VerticalAlignment.Center
            };
            ApplyFontToTextBlock(tb, meta);
            Grid.SetColumn(tb, 1);

            row.Children.Add(sizeLabel);
            row.Children.Add(tb);
            WaterfallPanel.Children.Add(row);
        }
    }

    // ── Glyph Grid ─────────────────────────────────────────────

    /// <summary>
    /// Rebuilds everything related to the Glyphs tab when a new font loads:
    /// the master glyph list, the Unicode-block sidebar, the functional
    /// category chips, and resets the active filters.
    /// </summary>
    private void BuildGlyphGrid()
    {
        _viewModel.GenerateGlyphItemsPublic();

        BuildBlockSidebar();
        BuildCategoryChips();
        ResetGlyphFilters();
        ApplyGlyphFilters();

        GlyphGrid.ContainerContentChanging -= GlyphGrid_ContainerContentChanging;
        GlyphGrid.ContainerContentChanging += GlyphGrid_ContainerContentChanging;
    }

    private void BuildBlockSidebar()
    {
        _glyphBlockEntries.Clear();

        var perBlockCounts = new Dictionary<string, (UnicodeBlocks.UnicodeBlock? Block, int Count)>();
        foreach (var item in _viewModel.GlyphItems)
        {
            var block = UnicodeBlocks.GetBlock(item.CodePoint);
            var key = block.Name;
            if (perBlockCounts.TryGetValue(key, out var existing))
            {
                perBlockCounts[key] = (existing.Block, existing.Count + 1);
            }
            else
            {
                perBlockCounts[key] = (block.Start >= 0 ? block : null, 1);
            }
        }

        _glyphBlockEntries.Add(new GlyphBlockEntry("All blocks", _viewModel.GlyphItems.Count, null));

        // Preserve the curated order from UnicodeBlocks.All; "Other" goes last.
        foreach (var b in UnicodeBlocks.All)
        {
            if (perBlockCounts.TryGetValue(b.Name, out var entry))
                _glyphBlockEntries.Add(new GlyphBlockEntry(b.Name, entry.Count, b));
        }
        if (perBlockCounts.TryGetValue("Other", out var other))
            _glyphBlockEntries.Add(new GlyphBlockEntry("Other", other.Count, null));

        GlyphBlockList.ItemsSource = _glyphBlockEntries;
    }

    private void BuildCategoryChips()
    {
        GlyphCategoryChips.Children.Clear();

        foreach (GlyphCategory cat in Enum.GetValues<GlyphCategory>())
        {
            var btn = new ToggleButton
            {
                Content = cat.ToString(),
                Tag = cat,
                MinWidth = 0,
                Padding = new Thickness(10, 4, 10, 4),
                IsChecked = cat == GlyphCategory.All
            };
            btn.Click += GlyphCategoryChip_Click;
            GlyphCategoryChips.Children.Add(btn);
        }
    }

    private void ResetGlyphFilters()
    {
        _suppressGlyphFilterEvents = true;
        try
        {
            _glyphCategoryFilter = GlyphCategory.All;
            _glyphBlockFilter = null;
            _glyphSearchText = string.Empty;

            foreach (var child in GlyphCategoryChips.Children)
            {
                if (child is ToggleButton tb && tb.Tag is GlyphCategory cat)
                    tb.IsChecked = cat == GlyphCategory.All;
            }

            if (_glyphBlockEntries.Count > 0)
                GlyphBlockList.SelectedIndex = 0;

            GlyphSearchBox.Text = string.Empty;
        }
        finally
        {
            _suppressGlyphFilterEvents = false;
        }
    }

    private void ApplyGlyphFilters()
    {
        IEnumerable<GlyphItem> view = _viewModel.GlyphItems;

        var blockFilter = _glyphBlockFilter;
        if (blockFilter is not null)
        {
            view = view.Where(g => blockFilter.Contains(g.CodePoint));
        }
        else if (GlyphBlockList.SelectedItem is GlyphBlockEntry entry && entry.Name == "Other")
        {
            view = view.Where(g => UnicodeBlocks.GetBlock(g.CodePoint).Start < 0);
        }

        if (_glyphCategoryFilter != GlyphCategory.All)
        {
            view = view.Where(g => GlyphCategoryClassifier.Classify(g.CodePoint) == _glyphCategoryFilter);
        }

        if (!string.IsNullOrWhiteSpace(_glyphSearchText))
        {
            var needle = _glyphSearchText.Trim();
            var matcher = BuildSearchMatcher(needle);
            view = view.Where(g => matcher(g));
        }

        var filtered = view.ToList();
        GlyphGrid.ItemsSource = filtered;
        GlyphCountText.Text = filtered.Count.ToString();
        GlyphDetailPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Builds a predicate that matches a glyph by raw character ("A"), hex code
    /// point ("U+00A0" / "00A0" / "0x00A0"), or decimal code point ("9731").
    /// </summary>
    private static Func<GlyphItem, bool> BuildSearchMatcher(string needle)
    {
        // Hex form: "U+xxxx" or "0xXXXX"
        if (needle.StartsWith("U+", StringComparison.OrdinalIgnoreCase)
            || needle.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var hex = needle[2..];
            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var cpHex))
                return g => g.CodePoint == cpHex;
        }

        // Bare hex (4+ digits, all hex)
        if (needle.Length >= 4 && needle.All(c => Uri.IsHexDigit(c)))
        {
            if (int.TryParse(needle, System.Globalization.NumberStyles.HexNumber, null, out var cpHex))
                return g => g.CodePoint == cpHex;
        }

        // Decimal
        if (int.TryParse(needle, out var cpDec))
            return g => g.CodePoint == cpDec;

        // Single character literal
        if (needle.Length <= 2)
        {
            try
            {
                var cp = char.ConvertToUtf32(needle, 0);
                return g => g.CodePoint == cp;
            }
            catch
            {
                // fall through
            }
        }

        // Substring on hex label (covers things like "F60" hitting U+1F600..U+1F60F)
        return g => g.UnicodeLabel.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private void GlyphGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Phase == 0 && _loadedFontFamily != null)
        {
            args.RegisterUpdateCallback((s, a) =>
            {
                if (a.ItemContainer.ContentTemplateRoot is StackPanel panel &&
                    panel.Children.Count > 0 &&
                    panel.Children[0] is TextBlock charBlock)
                {
                    charBlock.FontFamily = _loadedFontFamily;
                }
            });
        }
    }

    private void GlyphGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GlyphGrid.SelectedItem is GlyphItem glyph)
        {
            GlyphDetailPanel.Visibility = Visibility.Visible;
            SelectedGlyphChar.Text = glyph.Character;
            SelectedGlyphUnicode.Text = glyph.UnicodeLabel;
            var block = UnicodeBlocks.GetBlock(glyph.CodePoint);
            var category = GlyphCategoryClassifier.Classify(glyph.CodePoint);
            SelectedGlyphName.Text =
                $"Decimal: {glyph.CodePoint} · Block: {block.Name} · Category: {category}";

            if (_loadedFontFamily != null)
                SelectedGlyphChar.FontFamily = _loadedFontFamily;
        }
        else
        {
            GlyphDetailPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void GlyphBlockList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressGlyphFilterEvents) return;
        if (GlyphBlockList.SelectedItem is GlyphBlockEntry entry)
        {
            _glyphBlockFilter = entry.Block;
            ApplyGlyphFilters();
        }
    }

    private void GlyphCategoryChip_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressGlyphFilterEvents) return;
        if (sender is not ToggleButton btn || btn.Tag is not GlyphCategory selected) return;

        _suppressGlyphFilterEvents = true;
        try
        {
            // Single-select: clear any previous chip, force the clicked one on.
            foreach (var child in GlyphCategoryChips.Children)
            {
                if (child is ToggleButton other && other.Tag is GlyphCategory cat)
                    other.IsChecked = cat == selected;
            }
            _glyphCategoryFilter = selected;
        }
        finally
        {
            _suppressGlyphFilterEvents = false;
        }
        ApplyGlyphFilters();
    }

    private void GlyphSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_suppressGlyphFilterEvents) return;
        _glyphSearchText = sender.Text ?? string.Empty;
        ApplyGlyphFilters();
    }

    /// <summary>Sidebar row model.</summary>
    private sealed record GlyphBlockEntry(string Name, int Count, UnicodeBlocks.UnicodeBlock? Block);

    // ── Metadata ───────────────────────────────────────────────

    private void BuildMetadataView()
    {
        MetadataPanel.Children.Clear();
        var font = _viewModel.CurrentFont;
        if (font is null) return;
        var meta = font.Metadata;

        AddMetadataSection("General");
        AddMetadataRow("Family Name", meta.FamilyName);
        if (meta.TypographicFamilyName != meta.FamilyName)
            AddMetadataRow("Typographic Family", meta.TypographicFamilyName);
        AddMetadataRow("Subfamily", meta.SubfamilyName);
        AddMetadataRow("Full Name", meta.FullName);
        AddMetadataRow("PostScript Name", meta.PostScriptName);
        AddMetadataRow("Version", meta.Version);
        AddMetadataRow("Format", font.Format.ToString());
        AddMetadataRow("File Size", font.FormattedFileSize);
        AddMetadataRow("File Path", font.FilePath);
        if (font.FontCount > 1)
            AddMetadataRow("Font in Collection", $"{font.FontIndex + 1} of {font.FontCount}");

        AddMetadataSection("Metrics");
        AddMetadataRow("Glyphs", meta.GlyphCount.ToString());
        AddMetadataRow("Units per Em", meta.UnitsPerEm.ToString());
        AddMetadataRow("Weight", GetWeightName(meta.Weight));
        AddMetadataRow("Style", meta.IsItalic ? "Italic" : meta.IsOblique ? "Oblique" : "Normal");
        AddMetadataRow("Variable Font", meta.IsVariable ? "Yes" : "No");
        AddMetadataRow("Classification", meta.Classification.ToString());

        AddMetadataSection("Credits");
        AddMetadataRow("Designer", meta.Designer);
        AddMetadataRow("Vendor", meta.Vendor);
        AddMetadataRow("Copyright", meta.Copyright);
        AddMetadataRow("Trademark", meta.Trademark);

        AddMetadataSection("License");
        AddMetadataRow("License", meta.License);
        AddMetadataRow("License URL", meta.LicenseUrl);

        if (!string.IsNullOrWhiteSpace(meta.Description))
        {
            AddMetadataSection("Description");
            AddMetadataRow("", meta.Description);
        }
    }

    private void AddMetadataSection(string title)
    {
        MetadataPanel.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            Margin = new Thickness(0, 16, 0, 8)
        });
    }

    private void AddMetadataRow(string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        var border = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 1, 0, 1)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (!string.IsNullOrWhiteSpace(label))
        {
            var labelBlock = new TextBlock
            {
                Text = label,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(labelBlock, 0);
            grid.Children.Add(labelBlock);
        }

        var valueBlock = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.WrapWholeWords,
            IsTextSelectionEnabled = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(valueBlock, string.IsNullOrWhiteSpace(label) ? 0 : 1);
        if (string.IsNullOrWhiteSpace(label))
            Grid.SetColumnSpan(valueBlock, 2);
        grid.Children.Add(valueBlock);

        border.Child = grid;
        MetadataPanel.Children.Add(border);
    }

    // ── State Management ───────────────────────────────────────

    private void ShowState(bool empty = false, bool loading = false, bool error = false, bool content = false)
    {
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        LoadingState.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        ErrorState.Visibility = error ? Visibility.Visible : Visibility.Collapsed;
        FontContent.Visibility = content ? Visibility.Visible : Visibility.Collapsed;
        InstallSplitButton.IsEnabled = content && !string.IsNullOrWhiteSpace(_currentFilePath);
    }

    // ── Settings ────────────────────────────────────────────────

    private void ApplySettings()
    {
        if (RootGrid.XamlRoot != null)
            ((FrameworkElement)Content).RequestedTheme = _settings.Theme;
        else
            RootGrid.Loaded += (_, _) => ((FrameworkElement)Content).RequestedTheme = _settings.Theme;

        PreviewTextBox.Text = _settings.DefaultPreviewText;
        _viewModel.PreviewText = _settings.DefaultPreviewText;
        SetPreviewFontSize(_settings.DefaultFontSize);
        UpdateInstallButtonPresentation(GetSavedInstallTarget());
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dividerBrush = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"];
        var sectionStyle = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"];
        var captionStyle = (Style)Application.Current.Resources["CaptionTextBlockStyle"];
        var secondaryBrush = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

        // ── Helper: section header ──
        UIElement SectionHeader(string text) => new TextBlock
        {
            Text = text,
            Style = sectionStyle,
            Margin = new Thickness(0, 4, 0, 0)
        };

        UIElement Divider() => new Border
        {
            Height = 1,
            Background = dividerBrush,
            Margin = new Thickness(0, 4, 0, 4)
        };

        UIElement Description(string text) => new TextBlock
        {
            Text = text,
            Style = captionStyle,
            Foreground = secondaryBrush,
            TextWrapping = TextWrapping.WrapWholeWords,
            Margin = new Thickness(0, -8, 0, 0)
        };

        // ══════════════════════════════════════════════════════════
        // APPEARANCE
        // ══════════════════════════════════════════════════════════

        var themeCombo = new ComboBox
        {
            Header = "Theme",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new ComboBoxItem { Content = "System default", Tag = ElementTheme.Default },
                new ComboBoxItem { Content = "Light", Tag = ElementTheme.Light },
                new ComboBoxItem { Content = "Dark", Tag = ElementTheme.Dark }
            },
            SelectedIndex = (int)_settings.Theme
        };

        var backdropCombo = new ComboBox
        {
            Header = "Backdrop material",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new ComboBoxItem { Content = "Mica", Tag = 0 },
                new ComboBoxItem { Content = "Acrylic", Tag = 1 }
            },
            SelectedIndex = _settings.Backdrop
        };

        // ══════════════════════════════════════════════════════════
        // PREVIEW
        // ══════════════════════════════════════════════════════════

        var previewTextBox = new TextBox
        {
            Header = "Default preview text",
            Text = _settings.DefaultPreviewText,
            PlaceholderText = "Enter default preview text...",
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = false,
            MaxLength = 500
        };

        var fontSizeSlider = new Slider
        {
            Header = $"Default font size ({(int)_settings.DefaultFontSize}px)",
            Minimum = 8,
            Maximum = 120,
            Value = _settings.DefaultFontSize,
            StepFrequency = 1
        };
        fontSizeSlider.ValueChanged += (_, args) =>
            fontSizeSlider.Header = $"Default font size ({(int)args.NewValue}px)";

        var previewControlsToggle = new ToggleSwitch
        {
            Header = "Editable preview",
            IsOn = _settings.ShowPreviewControls
        };

        // ══════════════════════════════════════════════════════════
        // DISPLAY
        // ══════════════════════════════════════════════════════════

        var quickViewToggle = new ToggleSwitch
        {
            Header = "Quick view",
            IsOn = _settings.ShowQuickView
        };

        var waterfallToggle = new ToggleSwitch
        {
            Header = "Waterfall",
            IsOn = _settings.ShowWaterfall
        };

        var waterfallSizesBox = new TextBox
        {
            Header = "Waterfall sizes (comma-separated)",
            Text = _settings.WaterfallSizesRaw,
            PlaceholderText = "8,12,16,24,32,48,72",
            AcceptsReturn = false,
            MaxLength = 200
        };

        // ══════════════════════════════════════════════════════════
        // INSTALL
        // ══════════════════════════════════════════════════════════

        var installModeCombo = new ComboBox
        {
            Header = "Install target",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new ComboBoxItem { Content = "Current user", Tag = 0 },
                new ComboBoxItem { Content = "All users (requires admin)", Tag = 1 }
            },
            SelectedIndex = _settings.InstallMode
        };

        // ══════════════════════════════════════════════════════════
        // RESET
        // ══════════════════════════════════════════════════════════

        var resetButton = new HyperlinkButton
        {
            Content = "Reset all settings to defaults",
            Margin = new Thickness(0, 4, 0, 0)
        };
        bool resetRequested = false;
        resetButton.Click += (_, _) => resetRequested = true;

        // ══════════════════════════════════════════════════════════
        // LAYOUT
        // ══════════════════════════════════════════════════════════

        var panel = new StackPanel { Spacing = 12 };

        // Appearance
        panel.Children.Add(SectionHeader("Appearance"));
        panel.Children.Add(themeCombo);
        panel.Children.Add(backdropCombo);

        panel.Children.Add(Divider());

        // Preview
        panel.Children.Add(SectionHeader("Preview"));
        panel.Children.Add(previewTextBox);
        panel.Children.Add(fontSizeSlider);
        panel.Children.Add(previewControlsToggle);
        panel.Children.Add(Description("Show the editable text preview area with size slider. When off, only the waterfall is shown."));

        panel.Children.Add(Divider());

        // Display
        panel.Children.Add(SectionHeader("Display"));
        panel.Children.Add(quickViewToggle);
        panel.Children.Add(Description("Character set overview below the font header. Auto-shows when the window is small."));
        panel.Children.Add(waterfallToggle);
        panel.Children.Add(waterfallSizesBox);

        panel.Children.Add(Divider());

        // Install
        panel.Children.Add(SectionHeader("Install"));
        panel.Children.Add(installModeCombo);
        panel.Children.Add(Description("Select the default target used by the main Install button. All-users install copies to Windows\\Fonts and requires administrator privileges."));

        panel.Children.Add(Divider());

        // Reset
        panel.Children.Add(resetButton);

        panel.Children.Add(Divider());

        // About
        panel.Children.Add(SectionHeader("About"));

        string aboutVersion;
        try
        {
            var ver = Package.Current.Id.Version;
            aboutVersion = $"{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}";
        }
        catch
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            aboutVersion = asm?.ToString() ?? "0.0.0.0";
        }

        panel.Children.Add(new TextBlock
        {
            Text = $"Fontager Viewer  v{aboutVersion}",
            FontWeight = FontWeights.SemiBold,
            Style = (Style)Application.Current.Resources["BodyTextBlockStyle"]
        });
        panel.Children.Add(Description("A modern font viewer for Windows, built with WinUI 3."));
        panel.Children.Add(new TextBlock
        {
            Text = "Made by Yusuf Emre Albayrak",
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Margin = new Thickness(0, -4, 0, 0)
        });

        var githubLink = new HyperlinkButton
        {
            Content = "GitHub — ysfemreAlbyrk/Fontager",
            NavigateUri = new Uri("https://github.com/ysfemreAlbyrk/Fontager"),
            Padding = new Thickness(0),
            Margin = new Thickness(0, 2, 0, 0)
        };
        panel.Children.Add(githubLink);

        // ══════════════════════════════════════════════════════════
        // DIALOG
        // ══════════════════════════════════════════════════════════

        var contentContainer = new Grid { MinWidth = 580, HorizontalAlignment = HorizontalAlignment.Stretch };
        contentContainer.Children.Add(new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 560
        });

        var dialog = new ContentDialog
        {
            Title = "Settings",
            Content = contentContainer,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            if (resetRequested)
            {
                _settings.ResetToDefaults();
                ApplySettings();
                ApplyBackdrop();
                if (_viewModel.HasFont)
                    UpdateFontDisplay();
                return;
            }

            // Theme
            if (themeCombo.SelectedItem is ComboBoxItem st && st.Tag is ElementTheme theme)
            {
                _settings.Theme = theme;
                ((FrameworkElement)Content).RequestedTheme = theme;
            }

            // Backdrop
            if (backdropCombo.SelectedItem is ComboBoxItem sb && sb.Tag is int bv)
            {
                _settings.Backdrop = bv;
                ApplyBackdrop();
            }

            // Preview
            _settings.DefaultPreviewText = previewTextBox.Text;
            _settings.DefaultFontSize = fontSizeSlider.Value;
            _settings.ShowPreviewControls = previewControlsToggle.IsOn;

            // Display
            _settings.ShowQuickView = quickViewToggle.IsOn;
            _settings.ShowWaterfall = waterfallToggle.IsOn;
            _settings.WaterfallSizesRaw = waterfallSizesBox.Text;

            // Install
            if (installModeCombo.SelectedItem is ComboBoxItem si && si.Tag is int iv)
                _settings.InstallMode = iv;
            UpdateInstallButtonPresentation(GetSavedInstallTarget());

            // Apply to UI
            if (!_viewModel.HasFont)
            {
                PreviewTextBox.Text = _settings.DefaultPreviewText;
                SetPreviewFontSize(_settings.DefaultFontSize);
            }

            PreviewSection.Visibility = _settings.ShowPreviewControls
                ? Visibility.Visible : Visibility.Collapsed;

            if (_viewModel.HasFont)
            {
                QuickViewSection.Visibility = _settings.ShowQuickView ? Visibility.Visible : Visibility.Collapsed;
                if (_settings.ShowQuickView) BuildQuickView();

                WaterfallSection.Visibility = _settings.ShowWaterfall ? Visibility.Visible : Visibility.Collapsed;
                if (_settings.ShowWaterfall) BuildWaterfallView();
            }
        }
    }

    // ── Helpers ─────────────────────────────────────────────────

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

    private static string GetWeightName(int weight) => weight switch
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
