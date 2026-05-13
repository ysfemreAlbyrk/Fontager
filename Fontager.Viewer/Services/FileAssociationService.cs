using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace Fontager.Viewer.Services;

/// <summary>
/// Per-user file-association helpers for the .ttf extension.
///
/// Why this exists: the MSIX schema's <c>windows.fileTypeAssociation</c> extension
/// rejects <c>.ttf</c> as a reserved file type owned by the built-in Windows Font
/// Viewer. We can't claim it from inside <c>Package.appxmanifest</c>. For the
/// unpackaged / portable build of Fontager, however, we can write the per-user
/// "Open with" entry under HKCU so the user finds Fontager in the Explorer
/// "Open with..." menu without having to hunt for the executable.
///
/// All writes go under <c>HKCU\Software\Classes</c> only. We never touch HKLM,
/// never claim the default handler, and never modify other applications'
/// entries.
/// </summary>
internal static class FileAssociationService
{
    private const string ProgId = "Fontager.Viewer.ttf";
    private const string AppExeName = "Fontager.Viewer.exe";

    /// <summary>
    /// True when Fontager is running with a packaged (MSIX) identity. Under
    /// MSIX, HKCU writes are virtualized into the package container and have
    /// no effect on Explorer, so we surface the feature as disabled.
    /// </summary>
    public static bool IsRunningPackaged
    {
        get
        {
            try
            {
                _ = Package.Current.Id.FamilyName;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// True when an HKCU "Open with" entry for .ttf is already pointing at this
    /// Fontager executable.
    /// </summary>
    public static bool IsTtfRegistered()
    {
        try
        {
            using var openWith = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.ttf\OpenWithProgids", false);
            return openWith?.GetValue(ProgId) is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Adds Fontager to the "Open with..." list for .ttf files for the current
    /// user. Returns true on success. No-op (returns false) when running
    /// packaged because the writes would be virtualized.
    /// </summary>
    public static bool RegisterTtfForCurrentUser()
    {
        if (IsRunningPackaged) return false;

        var exePath = GetExecutablePath();
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return false;

        var openCommand = $"\"{exePath}\" \"%1\"";
        var iconRef = $"\"{exePath}\",0";

        // ProgID: HKCU\Software\Classes\Fontager.Viewer.ttf
        using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}", true))
        {
            progIdKey?.SetValue(string.Empty, "Font file (Fontager)");
            using var iconKey = progIdKey?.CreateSubKey("DefaultIcon", true);
            iconKey?.SetValue(string.Empty, iconRef);
            using var cmdKey = progIdKey?.CreateSubKey(@"shell\open\command", true);
            cmdKey?.SetValue(string.Empty, openCommand);
        }

        // Application registration: HKCU\Software\Classes\Applications\Fontager.Viewer.exe
        using (var appKey = Registry.CurrentUser.CreateSubKey(
            $@"Software\Classes\Applications\{AppExeName}", true))
        {
            appKey?.SetValue("FriendlyAppName", "Fontager Viewer");
            using var cmdKey = appKey?.CreateSubKey(@"shell\open\command", true);
            cmdKey?.SetValue(string.Empty, openCommand);
            using var supportedKey = appKey?.CreateSubKey("SupportedTypes", true);
            supportedKey?.SetValue(".ttf", string.Empty);
        }

        // Surface the ProgID in the .ttf "Open with..." list.
        using (var openWith = Registry.CurrentUser.CreateSubKey(
            @"Software\Classes\.ttf\OpenWithProgids", true))
        {
            openWith?.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        NotifyShellAssociationChanged();
        return true;
    }

    /// <summary>
    /// Reverses <see cref="RegisterTtfForCurrentUser"/>. Only deletes entries we
    /// own; never touches other applications.
    /// </summary>
    public static bool UnregisterTtfForCurrentUser()
    {
        if (IsRunningPackaged) return false;

        try
        {
            using (var openWith = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.ttf\OpenWithProgids", true))
            {
                if (openWith?.GetValue(ProgId) is not null)
                    openWith.DeleteValue(ProgId, throwOnMissingValue: false);
            }

            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(
                $@"Software\Classes\Applications\{AppExeName}", throwOnMissingSubKey: false);

            NotifyShellAssociationChanged();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetExecutablePath()
    {
        var module = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrEmpty(module) && File.Exists(module)) return module;

        return Path.Combine(AppContext.BaseDirectory, AppExeName);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    private static void NotifyShellAssociationChanged()
    {
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }
}
