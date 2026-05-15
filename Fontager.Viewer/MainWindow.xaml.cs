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
using Fontager.Viewer.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel;
using Windows.Graphics;
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
    private const string FontCacheFolderName = "FontCache";

    private string? _activeFontPath;
    private string? _currentFilePath;
    private int _currentFontIndex;
    private int _currentFontCount = 1;
    /// <summary>Full path of the cached preview font file (deleted before writing the next cache entry).</summary>
    private string? _cachedFontDiskPath;
    private bool _quickViewAutoShown; // true when Quick View was auto-shown due to small window

    // ── Glyph filtering state ──────────────────────────────────
    // The grid is the intersection of three filters: a Unicode block
    // (sidebar), a functional category (chips), and a free-text search.
    private readonly ObservableCollection<GlyphBlockEntry> _glyphBlockEntries = new();
    private GlyphCategory _glyphCategoryFilter = GlyphCategory.All;
    private UnicodeBlocks.UnicodeBlock? _glyphBlockFilter;
    private string _glyphSearchText = string.Empty;
    private bool _suppressGlyphFilterEvents;

    // Search input is debounced: a fast typist hitting the search box would
    // otherwise re-filter 11k+ glyphs and rebuild the GridView ItemsSource on
    // every keystroke. 150 ms is short enough to feel instant and long enough
    // to coalesce a typed word.
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _glyphSearchDebounceTimer;
    private const int GlyphSearchDebounceMs = 150;

    /// <summary>Coalesces custom title-bar passthrough recomputation (avoid resize storms).</summary>
    private bool _titleBarPassthroughScheduled;

    /// <summary>
    /// Last backdrop mode applied to <see cref="Window.SystemBackdrop"/> (0 Mica, 1 Acrylic,
    /// 2 Solid, 3 Mica Alt). Avoids replacing the backdrop instance on unrelated settings
    /// writes — that recreation flashes Mica/Acrylic.
    /// </summary>
    private int _appliedBackdropKind = int.MinValue;

    /// <summary>
    /// True when this process is running with an elevated administrator token
    /// (e.g. "Run as administrator"). Per-machine font install requires this.
    /// </summary>
    private readonly bool _isProcessElevated;

    public MainWindow()
    {
        InitializeComponent();
        _isProcessElevated = IsRunningElevated();

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

        // SettingsPage writes through to SettingsService control-by-control;
        // we listen here so the user sees theme/backdrop/preview changes
        // take effect the instant they tap a control (no Save button).
        _settings.Changed += OnSettingsChanged;

        // Auto-show/hide Quick View based on window size
        this.SizeChanged += OnWindowSizeChanged;
        this.SizeChanged += (_, _) => ScheduleTitleBarPassthroughUpdate();

        AppTitleBar.Loaded += (_, _) => ScheduleTitleBarPassthroughUpdate();
        AppTitleBar.SizeChanged += (_, _) => ScheduleTitleBarPassthroughUpdate();

        if (!string.IsNullOrEmpty(App.FontFilePath))
            _ = LoadFontFromPathAsync(App.FontFilePath, 0);
    }

    private bool _suppressSettingsChangedReaction;

    /// <summary>
    /// Re-applies window-level settings (theme, backdrop, preview visibility,
    /// glyph/waterfall sections) after the Settings page mutates them.
    /// Lightweight on purpose — heavy rebuilds (font display, glyph grid) are
    /// triggered only when the user navigates back from Settings.
    /// </summary>
    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (_suppressSettingsChangedReaction) return;

        try
        {
            // Theme + backdrop are window-level; cheap to re-apply on every
            // change.
            if (Content is FrameworkElement fe)
                fe.RequestedTheme = _settings.Theme;
            ApplyBackdrop();

            // Install button label tracks the saved target.
            UpdateInstallButtonPresentation(GetSavedInstallTarget());
            ApplyInstallElevatedUi();

            // Keep the main preview strip in sync with persisted defaults whenever
            // Settings changes (including while a font is loaded — previously only
            // the empty-state path refreshed DefaultPreviewText / DefaultFontSize).
            PreviewTextBox.Text = _settings.DefaultPreviewText;
            _viewModel.PreviewText = _settings.DefaultPreviewText;
            SetPreviewFontSize(_settings.DefaultFontSize);

            if (_viewModel.HasFont)
            {
                PreviewSection.Visibility = _settings.ShowPreviewControls
                    ? Visibility.Visible : Visibility.Collapsed;
                QuickViewSection.Visibility = _settings.ShowQuickView
                    ? Visibility.Visible : Visibility.Collapsed;
                WaterfallSection.Visibility = _settings.ShowWaterfall
                    ? Visibility.Visible : Visibility.Collapsed;
                BuildWaterfallView();
            }
        }
        catch
        {
            // Best-effort. Settings page must never crash MainWindow.
        }
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
    /// One passthrough update per UI frame — avoids O(n) WinRT work on every
    /// intermediate resize pixel when the user drags the window edge.
    /// </summary>
    private void ScheduleTitleBarPassthroughUpdate()
    {
        if (_titleBarPassthroughScheduled)
            return;
        _titleBarPassthroughScheduled = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _titleBarPassthroughScheduled = false;
            UpdateCustomTitleBarPassthroughRects();
        });
    }

    /// <summary>
    /// Marks caption-button safe zones and forwards hits for interactive title-bar
    /// controls per <see href="https://learn.microsoft.com/windows/apps/develop/title-bar">title-bar guidance</see>.
    /// </summary>
    private void UpdateCustomTitleBarPassthroughRects()
    {
        if (!ExtendsContentIntoTitleBar || AppTitleBar.XamlRoot is null)
            return;

        try
        {
            var scale = AppTitleBar.XamlRoot.RasterizationScale;
            var chrome = AppWindow.TitleBar;
            TitleBarLeftInsetColumn.Width = new GridLength(chrome.LeftInset / scale);
            TitleBarRightInsetColumn.Width = new GridLength(chrome.RightInset / scale);

            var rects = new List<RectInt32>();

            void AddIfInteractive(FrameworkElement? el)
            {
                if (el is null || el.Visibility != Visibility.Visible)
                    return;
                if (el.ActualWidth <= 1 || el.ActualHeight <= 1)
                    return;
                rects.Add(ToPhysicalPassthroughRect(el, scale));
            }

            AddIfInteractive(BackButton);
            AddIfInteractive(OpenButtonPanel);
            AddIfInteractive(SettingsButton);

            var src = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
            src.ClearRegionRects(NonClientRegionKind.Passthrough);
            if (rects.Count > 0)
                src.SetRegionRects(NonClientRegionKind.Passthrough, rects.ToArray());
        }
        catch
        {
            // Shells without full InputNonClientPointerSource support: skip silently.
        }
    }

    private static RectInt32 ToPhysicalPassthroughRect(FrameworkElement el, double scale)
    {
        var gt = el.TransformToVisual(null);
        var b = gt.TransformBounds(new Windows.Foundation.Rect(0, 0, el.ActualWidth, el.ActualHeight));
        return new RectInt32(
            (int)Math.Round(b.X * scale),
            (int)Math.Round(b.Y * scale),
            (int)Math.Round(b.Width * scale),
            (int)Math.Round(b.Height * scale));
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
        if (FileAssociationService.IsRunningPackaged)
        {
            try
            {
                var packagePath = Package.Current.InstalledLocation.Path;
                var packaged = Path.Combine(packagePath, relativePath);
                if (File.Exists(packaged)) return packaged;
            }
            catch
            {
                // Rare: packaged but Storage API unavailable — fall through.
            }
        }

        var baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, relativePath);
    }

    private void SetAppVersion()
    {
        string versionStr;
        if (FileAssociationService.IsRunningPackaged)
        {
            try
            {
                var version = Package.Current.Id.Version;
                versionStr = $"v{version.Major}.{version.Minor}.{version.Build}";
            }
            catch
            {
                versionStr = AssemblyVersionFallback();
            }
        }
        else
        {
            versionStr = AssemblyVersionFallback();
        }
        TitleBarVersion.Text = versionStr;
        if (EmptyStateVersion != null)
            EmptyStateVersion.Text = versionStr;
    }

    private static string AssemblyVersionFallback()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return asm != null ? $"v{asm.Major}.{asm.Minor}.{asm.Build}" : "v0.0.0";
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
        int mode = _settings.Backdrop;
        if (mode == 4) mode = 1; // legacy acrylic-thin tag
        if (mode is < 0 or > 3) mode = 0;

        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        switch (mode)
        {
            case 2:
                SystemBackdrop = null;
                RootGrid.Background = new SolidColorBrush(ResolveSolidBackdropColor());
                _appliedBackdropKind = 2;
                return;

            case 1:
                if (_appliedBackdropKind != 1)
                {
                    RootGrid.Background = transparent;
                    SystemBackdrop = new DesktopAcrylicBackdrop();
                }
                _appliedBackdropKind = 1;
                return;

            case 3:
                if (_appliedBackdropKind != 3)
                {
                    RootGrid.Background = transparent;
                    SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
                }
                _appliedBackdropKind = 3;
                return;

            default:
                if (_appliedBackdropKind != 0)
                {
                    RootGrid.Background = transparent;
                    SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
                }
                _appliedBackdropKind = 0;
                return;
        }
    }

    private Windows.UI.Color ResolveSolidBackdropColor()
    {
        if (Application.Current.Resources.TryGetValue("ApplicationPageBackgroundThemeBrush", out var o)
            && o is SolidColorBrush scb)
            return scb.Color;

        var theme = RootGrid.ActualTheme;
        var dark = theme == ElementTheme.Dark
            || (theme == ElementTheme.Default
                && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        return dark
            ? Windows.UI.Color.FromArgb(255, 32, 32, 32)
            : Windows.UI.Color.FromArgb(255, 243, 243, 243);
    }

    // ── File Open ──────────────────────────────────────────────

    private async void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        string? path = await PickFontFilePathAsync();
        if (!string.IsNullOrEmpty(path))
            await LoadFontFromPathAsync(path, 0);
    }

    /// <summary>
    /// Opens an Open File dialog and returns the chosen path. Uses the WinRT
    /// FileOpenPicker by default, but falls back to a Win32 IFileOpenDialog
    /// when running elevated because the WinRT picker fails under UAC.
    /// </summary>
    private async Task<string?> PickFontFilePathAsync()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        string[] extensions = [".ttf", ".otf", ".ttc", ".woff2"];

        if (!IsRunningElevated())
        {
            try
            {
                var picker = new FileOpenPicker
                {
                    ViewMode = PickerViewMode.List,
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary
                };
                InitializeWithWindow.Initialize(picker, hwnd);
                foreach (var ext in extensions)
                    picker.FileTypeFilter.Add(ext);

                var file = await picker.PickSingleFileAsync();
                return file?.Path;
            }
            catch
            {
                // Fall through to the Win32 path.
            }
        }

        return await Task.Run(() =>
            Win32FileDialog.PickSingleFile(hwnd, "Open font file", "Font files", extensions));
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

            _currentFilePath = filePath;
            _currentFontIndex = _viewModel.CurrentFont.FontIndex;
            _currentFontCount = _viewModel.CurrentFont.FontCount;

            // Use TypographicFamilyName (name ID 16) for XAML FontFamily resolution.
            // This is critical for fonts like Material Icons where name ID 1 differs
            // from the canonical family name that DirectWrite expects.
            var meta = _viewModel.CurrentFont.Metadata;
            var familyName = PickDirectWriteFamilyName(meta, filePath);

            // WinUI 3 preview fonts: ms-appx under the install dir (FontCache junction when
            // installed to Program Files) or ms-appdata when packaged; else family name after
            // AddFontResourceEx (see CacheFontForXamlAsync / CreateLoadedFontFamily).
            //
            // For .woff2 the cache path runs Woff2Decoder so the on-disk bytes
            // are SFNT — required for AddFontResourceEx and for DirectWrite.
            var (diskPath, msAppDataRelativePath, msAppxRelativePath) = await CacheFontForXamlAsync(filePath);

            // GDI session-private registration on the *cached* SFNT path:
            // AddFontResourceEx cannot consume WOFF2, so we register the
            // decoded copy. Required for any non-XAML preview surfaces
            // (Win32 controls, third-party hooks) to see the family name.
            _ = AddFontResourceEx(diskPath, FR_PRIVATE, IntPtr.Zero);
            _activeFontPath = diskPath;

            _loadedFontFamily = CreateLoadedFontFamily(
                familyName, diskPath, msAppDataRelativePath, msAppxRelativePath);

            // Glyph grid: see BuildGlyphGrid — GridView item templates do not
            // inherit FontFamily from the parent; ContainerContentChanging sets
            // the character TextBlock per realized cell.
            GlyphGrid.FontFamily = _loadedFontFamily;

            UpdateFontDisplay();
            ShowState(content: true);
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"Error: {ex.Message}";
            ShowState(error: true);
        }
    }

    /// <summary>
    /// DirectWrite picks a face using family name + weight + style. For a
    /// single font <i>file</i>, those axes must match the one embedded master
    /// or resolution falls back to Segoe UI. Prefer typographic/family names from
    /// the name table; fall back to PostScript name (often required for some CFF
    /// fonts) and finally the file stem.
    /// </summary>
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

    /// <summary>
    /// Builds a <see cref="FontFamily"/> WinUI can resolve for a cached font.
    /// Unpackaged: <c>ms-appx:///FontCache/…</c> when the cache file is under the install
    /// directory (direct or junction). Otherwise the family name after private GDI load.
    /// Packaged: <c>ms-appdata:///local/FontCache/…</c>.
    /// </summary>
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

        // Cached outside ms-appx reach; AddFontResourceEx already registered this process.
        return new FontFamily(family);
    }

    /// <summary>
    /// Stages the font for XAML preview: packaged apps use
    /// <see cref="ApplicationData.Current"/>/<c>FontCache</c> with
    /// <c>ms-appdata:///local/…</c>; unpackaged apps use
    /// <see cref="FontCacheSetup"/> (install-relative <c>FontCache</c>, junction when needed).
    /// For <c>.woff2</c>, decompresses to SFNT via <see cref="Woff2Decoder"/>.
    /// </summary>
    private async Task<(string DiskPath, string? MsAppDataRelativePath, string? MsAppxRelativePath)> CacheFontForXamlAsync(string sourceFilePath)
    {
        bool useMsAppData;
        string cacheDir;

        if (IsWindowsPackaged())
        {
            try
            {
                var localFolder = ApplicationData.Current.LocalFolder;
                var cacheFolder = await localFolder.CreateFolderAsync(FontCacheFolderName,
                    CreationCollisionOption.OpenIfExists);
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

    /// <summary>
    /// True when the process has a package identity (MSIX / sparse package).
    /// </summary>
    private static bool IsWindowsPackaged() => FileAssociationService.IsRunningPackaged;

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

        // Title bar (font name is in the main header; window title still shows file identity)
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

        // Install: Windows font setup does not accept WOFF2; only preview here.
        bool isWoff2 = font.Format == FontFormat.WebOpenFont;
        InstallSplitButton.Visibility = isWoff2 ? Visibility.Collapsed : Visibility.Visible;
        InstallNotSupportedMessage.Visibility = isWoff2 ? Visibility.Visible : Visibility.Collapsed;

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
        {
            element.FontFamily = _loadedFontFamily;
            // Single face in the cached file: requesting OS/2 weight/style often makes
            // DirectWrite miss the face and substitute Segoe UI. Outlines already encode the style.
            element.FontWeight = new Windows.UI.Text.FontWeight(400);
            element.FontStyle = Windows.UI.Text.FontStyle.Normal;
            return;
        }

        element.FontWeight = new Windows.UI.Text.FontWeight((ushort)meta.Weight);
        element.FontStyle = meta.IsItalic ? Windows.UI.Text.FontStyle.Italic
            : meta.IsOblique ? Windows.UI.Text.FontStyle.Oblique
            : Windows.UI.Text.FontStyle.Normal;
    }

    private void ApplyFontToTextBlock(TextBlock tb, FontMetadata meta)
    {
        if (_loadedFontFamily != null)
        {
            tb.FontFamily = _loadedFontFamily;
            tb.FontWeight = new Windows.UI.Text.FontWeight(400);
            tb.FontStyle = Windows.UI.Text.FontStyle.Normal;
            return;
        }

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


    // ── State Management ───────────────────────────────────────

    private void ShowState(bool empty = false, bool loading = false, bool error = false, bool content = false)
    {
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        LoadingState.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        ErrorState.Visibility = error ? Visibility.Visible : Visibility.Collapsed;
        FontContent.Visibility = content ? Visibility.Visible : Visibility.Collapsed;
        bool canInstall = content
                          && !string.IsNullOrWhiteSpace(_currentFilePath)
                          && _viewModel.CurrentFont?.Format != FontFormat.WebOpenFont;
        InstallSplitButton.IsEnabled = canInstall;

        if (error)
            AppWindow.Title = "Fontager";
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
        ApplyInstallElevatedUi();
    }

    // ── Settings navigation ────────────────────────────────────────
    // We treat the settings overlay as a Frame so SettingsPage gets the full
    // WinUI 3 page lifecycle (OnNavigatedTo, caching, etc.). The visual
    // swap is just toggling MainContentArea vs. SettingsFrame visibility —
    // there's no need for a Window-level Frame because we never navigate
    // anywhere else.

    /// <summary>
    /// Pushes the SettingsPage onto the SettingsFrame and hides the viewer.
    /// </summary>
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsButton.Visibility = Visibility.Collapsed;

        MainContentArea.Visibility = Visibility.Collapsed;
        SettingsFrame.Visibility = Visibility.Visible;
        BackButton.Visibility = Visibility.Visible;
        ScheduleTitleBarPassthroughUpdate();

        // Ensure the overlay Frame shares the window XamlRoot so navigated
        // pages and dialogs never see a null XamlRoot (WinRT throws).
        if (RootGrid.XamlRoot is not null)
            SettingsFrame.XamlRoot = RootGrid.XamlRoot;

        // Defer navigation one tick so the Frame is visible and laid out;
        // navigating while the subtree was Collapsed contributed to first-click
        // failures and WinRT InvalidOperationException noise.
        var elevated = _isProcessElevated;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal,
            () => SettingsFrame.Navigate(typeof(SettingsPage), elevated));
    }

    /// <summary>
    /// Custom chrome back — <see cref="AnimatedIcon"/> over <c>AnimatedBackVisualSource</c>.
    /// </summary>
    private void CustomTitleBarBack_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsFrame.Visibility != Visibility.Visible)
            return;
        CloseSettingsOverlay();
    }

    private void BackButton_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimatedIcon.SetState(BackAnimatedIcon, "PointerOver");
    }

    private void BackButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimatedIcon.SetState(BackAnimatedIcon, "Normal");
    }

    /// <summary>
    /// Closes the SettingsPage, returns the user to the viewer, and triggers
    /// the heavier "post-settings" refresh (font display + glyph grid + nav
    /// state) that the per-change SettingsChanged handler intentionally skips.
    /// </summary>
    private void CloseSettingsOverlay()
    {
        SettingsFrame.Visibility = Visibility.Collapsed;
        SettingsFrame.Content = null;

        BackButton.Visibility = Visibility.Collapsed;
        AnimatedIcon.SetState(BackAnimatedIcon, "Normal");
        SettingsButton.Visibility = Visibility.Visible;
        ScheduleTitleBarPassthroughUpdate();
        MainContentArea.Visibility = Visibility.Visible;

        _suppressSettingsChangedReaction = true;
        try
        {
            if (_viewModel.HasFont)
                UpdateFontDisplay();
            UpdateInstallButtonPresentation(GetSavedInstallTarget());
            ApplyInstallElevatedUi();
        }
        finally
        {
            _suppressSettingsChangedReaction = false;
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

    private async Task ShowSuccessDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = BuildDialogHeroPanel(
                "\uE73E",
                ResolveThemeBrush("SystemFillColorSuccessBrush", Microsoft.UI.Colors.ForestGreen),
                message),
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    /// <summary>
    /// After a successful font install, shows the success dialog briefly then exits the process.
    /// The dialog is dismissed programmatically after one second so the user does not need to tap OK.
    /// </summary>
    private async Task ShowInstallSuccessThenExitAppAsync(string title, string message)
    {
        var dq = DispatcherQueue;
        var dialog = new ContentDialog
        {
            Title = title,
            Content = BuildDialogHeroPanel(
                "\uE73E",
                ResolveThemeBrush("SystemFillColorSuccessBrush", Microsoft.UI.Colors.ForestGreen),
                message),
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
            // Hide() or user dismiss can complete or fault the operation; exit regardless.
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
            Content = BuildDialogHeroPanel(
                "\uE7BA",
                ResolveThemeBrush("SystemFillColorCautionBrush", Microsoft.UI.Colors.DarkOrange),
                message),
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
            Content = BuildDialogHeroPanel(
                "\uE783",
                ResolveThemeBrush("SystemFillColorCriticalBrush", Microsoft.UI.Colors.Firebrick),
                message),
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

    private static Brush ResolveThemeBrush(string resourceKey, Windows.UI.Color fallback)
    {
        if (Application.Current.Resources.TryGetValue(resourceKey, out var o) && o is Brush br)
            return br;
        return new SolidColorBrush(fallback);
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
