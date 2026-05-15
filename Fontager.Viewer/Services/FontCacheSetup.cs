using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Fontager.Viewer.Services;

/// <summary>
/// WinUI preview fonts must be referenced as <c>ms-appx:///FontCache/…</c>, which only
/// resolves under the install directory. Program Files is not writable, so the installer
/// (or this helper) provides a directory junction <c>{app}\FontCache</c> →
/// <c>%ProgramData%\Fontager\FontCache</c>. Dev builds use a normal writable subfolder.
/// </summary>
internal static class FontCacheSetup
{
    public const string FolderName = "FontCache";

    private const int SymbolicLinkFlagDirectory = 0x1;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);

    /// <summary>Call once at startup before any font preview.</summary>
    public static string EnsureWritableCacheDirectory()
    {
        var installCache = Path.Combine(AppContext.BaseDirectory, FolderName);
        if (IsWritableDirectory(installCache))
            return installCache;

        var programDataTarget = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Fontager",
            FolderName);
        Directory.CreateDirectory(programDataTarget);

        if (!Directory.Exists(installCache))
            TryCreateDirectoryJunction(installCache, programDataTarget);

        if (IsWritableDirectory(installCache))
            return installCache;

        // Portable / junction failed — write directly under ProgramData (ms-appx unavailable).
        return programDataTarget;
    }

    public static bool IsUnderInstallDirectory(string fullPath)
    {
        var root = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var path = Path.GetFullPath(fullPath);
        return path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || path.Equals(root, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWritableDirectory(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryCreateDirectoryJunction(string linkPath, string targetPath)
    {
        try
        {
            if (CreateSymbolicLink(linkPath, targetPath, SymbolicLinkFlagDirectory))
                return;
        }
        catch
        {
            // Fall through to mklink (installer usually creates the junction with admin).
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
        }
        catch
        {
            // Non-fatal; caller falls back to ProgramData path.
        }
    }
}
