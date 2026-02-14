using System;
using System.IO;
using Fontager.Core.Services;
using Fontager.Viewer.Services;
using Fontager.Viewer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

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
    /// The font file path passed via command-line arguments (if any).
    /// </summary>
    public static string? FontFilePath { get; private set; }

    public App()
    {
        InitializeComponent();

        // Configure DI
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);
        Services = serviceCollection.BuildServiceProvider();

        // Parse command-line arguments for font file path
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1)
        {
            var candidatePath = args[1];
            // Handle quoted paths
            candidatePath = candidatePath.Trim('"');

            if (File.Exists(candidatePath))
            {
                FontFilePath = candidatePath;
            }
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core services
        services.AddSingleton<IFontService, FontService>();

        // App services
        services.AddSingleton<SettingsService>();

        // ViewModels
        services.AddTransient<FontViewerViewModel>();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
