using System;
using System.Linq;
using Fontager.Core.Services;

namespace Fontager.Viewer.Services;

/// <summary>
/// Handles <c>--install-all-users</c> in a short-lived elevated process (no main window).
/// </summary>
internal static class ElevatedInstallCommandLine
{
    public const string InstallAllUsersFlag = "--install-all-users";
    private const string SourceFlag = "--source";
    private const string NameFlag = "--name";
    private const string OverwriteFlag = "--overwrite";

    /// <summary>Returns true if the process handled the command line and exited.</summary>
    public static bool TryExecuteAndExit(string[] args)
    {
        if (!args.Contains(InstallAllUsersFlag, StringComparer.OrdinalIgnoreCase))
            return false;

        string? source = null;
        string? displayName = null;
        var overwrite = false;

        for (var i = 0; i < args.Length; i++)
        {
            if (EqualsFlag(args, i, SourceFlag) && i + 1 < args.Length)
                source = args[++i];
            else if (EqualsFlag(args, i, NameFlag) && i + 1 < args.Length)
                displayName = args[++i];
            else if (EqualsFlag(args, i, OverwriteFlag))
                overwrite = true;
        }

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(displayName))
        {
            Environment.Exit(1); // ExitError
            return true;
        }

        var installer = new Fontager.Core.Services.FontInstallerService();
        var code = installer.InstallForAllUsersSynchronous(source, displayName, overwrite);
        Environment.Exit(code);
        return true;
    }

    public static string BuildArguments(string sourcePath, string displayName, bool overwrite)
    {
        var source = Quote(sourcePath);
        var name = Quote(displayName);
        var args = $"{InstallAllUsersFlag} {SourceFlag} {source} {NameFlag} {name}";
        if (overwrite)
            args += $" {OverwriteFlag}";
        return args;
    }

    private static string Quote(string value) =>
        value.Contains(' ') ? $"\"{value}\"" : value;

    private static bool EqualsFlag(string[] args, int index, string flag) =>
        string.Equals(args[index], flag, StringComparison.OrdinalIgnoreCase);
}

