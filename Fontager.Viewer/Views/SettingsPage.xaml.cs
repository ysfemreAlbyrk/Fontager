using System;
using System.IO;
using Fontager.Core.Helpers;
using Fontager.Core.Services;
using Fontager.Viewer.Services;
using Fontager.Viewer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace Fontager.Viewer.Views;

/// <summary>
/// Full-window Settings page using proper MVVM.
/// </summary>
public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyAboutLogo();

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

    private async void RunAsAdminToggle_Toggled(object sender, RoutedEventArgs e)
    {
        var wantAdmin = RunAsAdminToggle.IsOn;
        var previous = ViewModel.RunAsAdministrator;
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
            RunAsAdminToggle.IsOn = previous; // reverts toggle
            return;
        }

        ViewModel.RunAsAdministrator = wantAdmin;

        if (wantAdmin == ViewModel.IsProcessElevated)
            return;

        ProcessElevationHelper.RestartWithElevation(wantAdmin);
    }

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
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

        try
        {
            ViewModel.ResetToDefaults();
            ApplyAboutLogo(); // re-resolve logo if needed
        }
        catch
        {
            // Best effort
        }
    }

    private async void UpdateAvailableButton_Click(object sender, RoutedEventArgs e)
    {
        var url = ViewModel.LatestReleaseUrl;
        if (!string.IsNullOrEmpty(url))
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        }
    }

    private async void ManualCheckButton_Click(object sender, RoutedEventArgs e)
    {
        ManualCheckButton.Visibility = Visibility.Collapsed;
        UpdateProgressRing.Visibility = Visibility.Visible;
        UpdateProgressRing.IsActive = true;

        try
        {
            var updateService = App.Services.GetRequiredService<UpdateCheckService>();
            var result = await updateService.CheckForUpdatesAsync(forceCheck: true);

            ViewModel.NotifyUpdatePropertiesChanged();

            var xamlRoot = this.XamlRoot ?? (Content as FrameworkElement)?.XamlRoot;
            if (xamlRoot is null) return;

            if (result.IsUpdateAvailable)
            {
                var dialog = new ContentDialog
                {
                    Title = "Update Available",
                    Content = $"A new version ({result.LatestVersion}) of Fontager is available. Would you like to open the download page?",
                    PrimaryButtonText = "Download",
                    CloseButtonText = "Close",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = xamlRoot
                };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    await Windows.System.Launcher.LaunchUriAsync(new Uri(result.ReleaseUrl));
                }
            }
            else
            {
                var dialog = new ContentDialog
                {
                    Title = "Up to Date",
                    Content = "You are running the latest version of Fontager.",
                    CloseButtonText = "OK",
                    XamlRoot = xamlRoot
                };
                await dialog.ShowAsync();
            }
        }
        catch (Exception ex)
        {
            var xamlRoot = this.XamlRoot ?? (Content as FrameworkElement)?.XamlRoot;
            if (xamlRoot is not null)
            {
                var dialog = new ContentDialog
                {
                    Title = "Error",
                    Content = $"Failed to check for updates: {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = xamlRoot
                };
                await dialog.ShowAsync();
            }
        }
        finally
        {
            UpdateProgressRing.IsActive = false;
            UpdateProgressRing.Visibility = Visibility.Collapsed;
            ManualCheckButton.Visibility = Visibility.Visible;
        }
    }
}
