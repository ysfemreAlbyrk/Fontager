using System;
using System.IO;
using Fontager.Core.Helpers;
using Fontager.Core.Services;
using Fontager.Viewer.Services;
using Fontager.Viewer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace Fontager.Viewer;

/// <summary>
/// Application entry point with DI container and command-line argument handling.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    /// <summary>
    /// The DI service provider for the application.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// The font file path passed via command-line or file activation (if any).
    /// </summary>
    public static string? FontFilePath { get; private set; }

    /// <summary>
    /// The active MainWindow instance of the application.
    /// </summary>
    public static MainWindow? MainWindowInstance { get; set; }

    public App()
    {
        // Bind Core ProcessElevationHelper's ExitAction to WinUI's clean shutdown
        Fontager.Core.Helpers.ProcessElevationHelper.ExitAction = ExitOnElevationRestart;

        if (ElevatedInstallCommandLine.TryExecuteAndExit(Environment.GetCommandLineArgs()))
            return;

        InitializeComponent();

        if (!Fontager.Core.Services.FileAssociationService.IsRunningPackaged)
            FontCacheSetup.EnsureWritableCacheDirectory();

        // Configure DI
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);
        Services = serviceCollection.BuildServiceProvider();

        ResolveFontFilePath();
    }

    /// <summary>
    /// Resolves the font file path from command-line args or file activation.
    /// File activation is used when user double-clicks a .ttf/.otf/.ttc file with Fontager set as default.
    /// </summary>
    private static void ResolveFontFilePath()
    {
        // 1. Try rich activation (file association double-click)
        try
        {
            var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activatedArgs.Kind == ExtendedActivationKind.File)
            {
                var fileArgs = (FileActivatedEventArgs)activatedArgs.Data;
                if (fileArgs.Files.Count > 0 && fileArgs.Files[0] is Windows.Storage.StorageFile storageFile)
                {
                    var path = storageFile.Path;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        FontFilePath = path;
                        return;
                    }
                }
            }
        }
        catch
        {
            // Fall through to command-line
        }

        // 2. Fall back to command-line arguments
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1)
        {
            var candidatePath = args[1].Trim('"');
            if (File.Exists(candidatePath))
                FontFilePath = candidatePath;
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core services
        services.AddSingleton<IFontService, FontService>();
        services.AddSingleton<IFontInstallerService, FontInstallerService>();

        // App services
        services.AddSingleton<SettingsService>();
        services.AddSingleton<UpdateCheckService>();

        // ViewModels
        // Singleton so font state survives Settings navigation and page cache restores.
        services.AddSingleton<FontViewerViewModel>();
        services.AddTransient<SettingsViewModel>();
    }


    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var settings = Services.GetRequiredService<SettingsService>();
        if (ProcessElevationHelper.TryRelaunchElevatedOnStartup(settings.RunAsAdministrator))
            return;

        _window = new MainWindow();
        _window.Activate();
    }

    /// <summary>Called before exiting to relaunch with different elevation.</summary>
    public void ExitOnElevationRestart() => Exit();
}
