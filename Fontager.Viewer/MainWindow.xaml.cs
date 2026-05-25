using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Fontager.Core.Helpers;
using Fontager.Core.Services;
using Fontager.Viewer.Services;
using Fontager.Viewer.ViewModels;
using Fontager.Viewer.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel;
using Windows.Graphics;
using Windows.Storage;
using WinRT.Interop;

namespace Fontager.Viewer;

public sealed partial class MainWindow : Window
{
    private readonly SettingsService _settings;
    private readonly bool _isProcessElevated;
    private int _appliedBackdropKind = int.MinValue;
    private bool _titleBarPassthroughScheduled;

    public MainWindow()
    {
        InitializeComponent();
        App.MainWindowInstance = this;
        _isProcessElevated = ProcessElevationHelper.IsRunningElevated();

        _settings = App.Services.GetRequiredService<SettingsService>();

        ConfigureWindow();
        ApplySettings();
        ApplyBackdrop();

        RootGrid.AllowDrop = true;
        RootGrid.DragOver += RootGrid_DragOver;
        RootGrid.Drop += RootGrid_Drop;

        SetAppVersion();

        _settings.Changed += OnSettingsChanged;

        this.Closed += OnWindowClosed;
        this.SizeChanged += (_, _) => ScheduleTitleBarPassthroughUpdate();

        AppTitleBar.Loaded += (_, _) => ScheduleTitleBarPassthroughUpdate();
        AppTitleBar.SizeChanged += (_, _) => ScheduleTitleBarPassthroughUpdate();

        RootFrame.Navigated += OnRootFrameNavigated;

        // Navigate to the initial page
        RootFrame.Navigate(typeof(FontViewerPage), App.FontFilePath);

        CheckForUpdatesOnStartup();
    }

    private void OnRootFrameNavigated(object sender, NavigationEventArgs e)
    {
        BackButton.Visibility = RootFrame.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.Visibility = (RootFrame.Content is SettingsPage) ? Visibility.Collapsed : Visibility.Visible;
        ScheduleTitleBarPassthroughUpdate();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        try
        {
            if (Content is FrameworkElement fe)
                fe.RequestedTheme = _settings.Theme;
            ApplyBackdrop();
        }
        catch
        {
            // Best-effort
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        try
        {
            if (AppWindow.Presenter is OverlappedPresenter overlappedPresenter)
            {
                bool isMaximized = overlappedPresenter.State == OverlappedPresenterState.Maximized;
                _settings.WindowMaximized = isMaximized;

                if (overlappedPresenter.State == OverlappedPresenterState.Restored)
                {
                    _settings.WindowWidth = AppWindow.Size.Width;
                    _settings.WindowHeight = AppWindow.Size.Height;
                    _settings.WindowX = AppWindow.Position.X;
                    _settings.WindowY = AppWindow.Position.Y;
                }
            }
        }
        catch
        {
            // Best effort
        }
    }

    private void ConfigureWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(hwnd);
        var scale = dpi / 96.0;

        int width = _settings.WindowWidth ?? (int)(850 * scale);
        int height = _settings.WindowHeight ?? (int)(600 * scale);

        if (_settings.WindowX.HasValue && _settings.WindowY.HasValue)
        {
            var x = _settings.WindowX.Value;
            var y = _settings.WindowY.Value;

            if (IsPositionOnScreen(x, y, width, height))
            {
                AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
            }
            else
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
            }
        }
        else
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        }

        AppWindow.Title = "Fontager";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        SetMinimumWindowSize(600, 266);

        ApplyWindowIcon();
        AllowDragDropFromLowerIntegrity();

        if (_settings.WindowMaximized)
        {
            if (AppWindow.Presenter is OverlappedPresenter overlappedPresenter)
            {
                overlappedPresenter.Maximize();
            }
        }
    }

    private bool IsPositionOnScreen(int x, int y, int width, int height)
    {
        try
        {
            var displayAreas = DisplayArea.FindAll();
            foreach (var area in displayAreas)
            {
                var bounds = area.OuterBounds;
                if (x < bounds.X + bounds.Width &&
                    x + width > bounds.X &&
                    y < bounds.Y + bounds.Height &&
                    y + height > bounds.Y)
                {
                    return true;
                }
            }
        }
        catch
        {
            return true;
        }
        return false;
    }

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
            AddIfInteractive(UpdateNotificationButton);

            var src = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
            src.ClearRegionRects(NonClientRegionKind.Passthrough);
            if (rects.Count > 0)
                src.SetRegionRects(NonClientRegionKind.Passthrough, rects.ToArray());
        }
        catch
        {
            // Skip on older platforms/shells without support
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

    private void AllowDragDropFromLowerIntegrity()
    {
        if (!_isProcessElevated) return;

        var hwnd = WindowNative.GetWindowHandle(this);
        ChangeWindowMessageFilterEx(hwnd, WM_DROPFILES, MSGFLT_ALLOW, IntPtr.Zero);
        ChangeWindowMessageFilterEx(hwnd, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);
        ChangeWindowMessageFilterEx(hwnd, WM_COPYGLOBALDATA, MSGFLT_ALLOW, IntPtr.Zero);
    }

    private void ApplyWindowIcon()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        string iconPath = ResolveAssetPath("Assets\\Logo.ico");

        try
        {
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }
        }
        catch { }

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
            catch { }
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
    }

    private static string AssemblyVersionFallback()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return asm != null ? $"v{asm.Major}.{asm.Minor}.{asm.Build}" : "v0.0.0";
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
        if (mode == 4) mode = 1;
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

    private void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (RootFrame.Content is FontViewerPage viewerPage)
        {
            _ = viewerPage.TriggerFileOpenPickerAsync();
        }
        else
        {
            RootFrame.Navigate(typeof(FontViewerPage));
        }
    }

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
        if (items.Count > 0 && items[0] is Windows.Storage.StorageFile file && App.Services.GetRequiredService<IFontService>().IsSupportedFont(file.Path))
        {
            if (RootFrame.Content is FontViewerPage viewerPage)
            {
                await viewerPage.LoadFontFromPathAsync(file.Path, 0);
            }
            else
            {
                RootFrame.Navigate(typeof(FontViewerPage), file.Path);
            }
        }
    }

    private void ApplySettings()
    {
        if (RootGrid.XamlRoot != null)
            ((FrameworkElement)Content).RequestedTheme = _settings.Theme;
        else
            RootGrid.Loaded += (_, _) => ((FrameworkElement)Content).RequestedTheme = _settings.Theme;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        RootFrame.Navigate(typeof(SettingsPage), _isProcessElevated);
    }

    private void CustomTitleBarBack_Click(object sender, RoutedEventArgs e)
    {
        if (RootFrame.CanGoBack)
        {
            RootFrame.GoBack();
        }
    }

    private void BackButton_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimatedIcon.SetState(BackAnimatedIcon, "PointerOver");
    }

    private void BackButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimatedIcon.SetState(BackAnimatedIcon, "Normal");
    }

    private async void CheckForUpdatesOnStartup()
    {
        if (!_settings.IsUpdateNotificationEnabled) return;

        try
        {
            var updateService = App.Services.GetRequiredService<UpdateCheckService>();
            var result = await updateService.CheckForUpdatesAsync(forceCheck: false);

            if (result.IsUpdateAvailable)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateNotificationButton.Visibility = Visibility.Visible;
                    ScheduleTitleBarPassthroughUpdate();
                });
            }
        }
        catch { }
    }

    private async void UpdateNotificationButton_Click(object sender, RoutedEventArgs e)
    {
        var latestVersion = _settings.LatestAvailableVersion;
        var releaseUrl = _settings.LatestReleaseUrl;

        var dialog = new ContentDialog
        {
            Title = "Update Available",
            Content = $"A new version of Fontager ({latestVersion}) is available! Would you like to open the download page?",
            PrimaryButtonText = "Yes",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(releaseUrl));
        }
    }

    // Win32 Interop P/Invokes and constants
    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint message, uint action, IntPtr pChangeFilterStruct);

    private const uint MSGFLT_ALLOW = 1;
    private const uint WM_DROPFILES = 0x0233;
    private const uint WM_COPYDATA = 0x004A;
    private const uint WM_COPYGLOBALDATA = 0x0049;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const uint WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;

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
}
