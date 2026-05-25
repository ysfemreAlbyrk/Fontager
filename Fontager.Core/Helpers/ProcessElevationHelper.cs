using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;

namespace Fontager.Core.Helpers;

/// <summary>
/// Decoupled utility for verifying process elevation and performing UAC administrative restarts.
/// </summary>
public static class ProcessElevationHelper
{
    private const int ErrorCancelled = 1223;
    public const int ExitSuccess = 0;
    public const int ExitError = 1;
    public const int ExitAlreadyExists = 2;

    /// <summary>
    /// Callback hooked by the UI layer to cleanly shut down the application after triggering a restart.
    /// </summary>
    public static Action? ExitAction { get; set; }

    public static bool IsRunningElevated()
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
    /// When the user enabled “run as administrator” but this instance is not elevated,
    /// relaunch elevated (UAC). Returns true if the current process should exit.
    /// </summary>
    public static bool TryRelaunchElevatedOnStartup(bool runAsAdministrator)
    {
        if (!runAsAdministrator || IsRunningElevated())
            return false;

        try
        {
            RestartWithElevation(wantElevation: true);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return false;
        }
    }

    /// <summary>
    /// Runs machine-wide install in a one-shot elevated child. Returns exit code,
    /// or <c>-1</c> if the user cancelled UAC.
    /// </summary>
    public static int TryInstallForAllUsersElevated(string sourcePath, string displayName, bool overwrite)
    {
        try
        {
            var args = BuildElevatedInstallerArguments(sourcePath, displayName, overwrite);
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = GetExecutablePath(),
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas",
            });

            if (process is null)
                return ExitError;

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return -1;
        }
    }

    public static void RestartWithElevation(bool wantElevation)
    {
        if (wantElevation)
        {
            if (!IsRunningElevated())
                RestartElevated();
        }
        else
        {
            if (IsRunningElevated())
                RestartDeElevated();
        }
    }

    private static void RestartElevated()
    {
        var exe = GetExecutablePath();
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = BuildArgumentString(),
            UseShellExecute = true,
            Verb = "runas",
        }) ?? throw new InvalidOperationException("Failed to start elevated process.");

        RequestAppExit();
    }

    private static void RestartDeElevated()
    {
        var exe = GetExecutablePath();
        var args = BuildArgumentString();
        var explorerArgs = string.IsNullOrEmpty(args) ? $"\"{exe}\"" : $"\"{exe}\" {args}";
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = explorerArgs,
            UseShellExecute = true,
        }) ?? throw new InvalidOperationException("Failed to start de-elevated process.");

        RequestAppExit();
    }

    private static string GetExecutablePath() =>
        Environment.ProcessPath
        ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? throw new InvalidOperationException("Cannot resolve application path.");

    private static string BuildArgumentString()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (args.Length == 0)
            return string.Empty;

        return string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
    }

    private static void RequestAppExit()
    {
        if (ExitAction is not null)
            ExitAction();
        else
            Environment.Exit(0);
    }

    private static string BuildElevatedInstallerArguments(string sourcePath, string displayName, bool overwrite)
    {
        var source = Quote(sourcePath);
        var name = Quote(displayName);
        var args = $"--install-all-users --source {source} --name {name}";
        if (overwrite)
            args += " --overwrite";
        return args;
    }

    private static string Quote(string value) =>
        value.Contains(' ') ? $"\"{value}\"" : value;
}
