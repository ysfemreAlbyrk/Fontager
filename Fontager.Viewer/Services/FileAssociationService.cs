using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Fontager.Viewer.Services;

/// <summary>
/// Per-user file-association helpers for Fontager's supported font formats
/// (<c>.ttf</c>, <c>.otf</c>, <c>.ttc</c>, <c>.woff2</c>).
///
/// <para>
/// We never claim the default handler and never write under HKLM. All entries
/// go under <c>HKCU\Software\Classes</c> so the user (a) doesn't need
/// administrator rights and (b) can fully reverse the change from inside the
/// Fontager Settings dialog.
/// </para>
///
/// <para>
/// <b>Why <c>.ttf</c> is special:</b> the MSIX
/// <c>windows.fileTypeAssociation</c> manifest schema rejects <c>.ttf</c>
/// (it's on Windows' reserved list, owned by the built-in Font Viewer).
/// For the unpackaged build (see <c>docs/research/packaging-decision.md</c>)
/// we can still add a per-user "Open with..." entry — which is what this
/// service does for all four extensions in one shot.
/// </para>
///
/// <para>
/// <b>Note on Microsoft Store distribution:</b> we are deliberately NOT
/// pursuing the Store at this stage. If we revisit it later, the path is
/// either (a) re-enable MSIX (and lose the <c>.ttf</c> entry here, because
/// the manifest still won't allow it), or (b) ship the unpackaged build via
/// the Store as a Win32 app — see the packaging-decision doc for the
/// trade-offs.
/// </para>
/// </summary>
internal static class FileAssociationService
{
    /// <summary>Win32: buffer too small — means the process has a package identity.</summary>
    private const int ErrorInsufficientBuffer = 122;

    private static readonly Lazy<bool> s_isRunningPackaged = new(ComputeIsRunningPackaged);

    /// <summary>Unified ProgID covering all four font extensions.</summary>
    private const string ProgId = "Fontager.Viewer.font";

    /// <summary>Legacy ProgID from the .ttf-only era; cleaned up on register/unregister.</summary>
    private const string LegacyTtfProgId = "Fontager.Viewer.ttf";

    /// <summary>Previous shipped host name — registry cleanup when renaming the executable.</summary>
    private const string LegacyApplicationExeName = "Fontager.Viewer.exe";

    /// <summary>Actual process image file name (e.g. <c>Fontager Viewer.exe</c>).</summary>
    private static string AppExeFileName =>
        string.IsNullOrEmpty(Environment.ProcessPath)
            ? "Fontager Viewer.exe"
            : Path.GetFileName(Environment.ProcessPath);

    /// <summary>
    /// All file extensions Fontager wants to be a candidate for in the
    /// Explorer "Open with..." menu. Lower-cased, includes the leading dot.
    /// </summary>
    public static IReadOnlyList<string> SupportedExtensions { get; } = [".ttf", ".otf", ".ttc", ".woff2"];

    /// <summary>
    /// True when Fontager is running with a packaged (MSIX) identity. Under
    /// MSIX, HKCU writes are virtualized into the package container and have
    /// no effect on Explorer, so we surface the feature as disabled. With
    /// the unpackaged build this is normally <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Uses <c>GetCurrentPackageFullName</c> instead of <c>Package.Current</c>
    /// so unpackaged runs do not throw <see cref="InvalidOperationException"/>
    /// (debugger first-chance noise and slower startup).
    /// </remarks>
    public static bool IsRunningPackaged => s_isRunningPackaged.Value;

    private static bool ComputeIsRunningPackaged()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, IntPtr.Zero) == ErrorInsufficientBuffer;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);

    /// <summary>
    /// True when the Fontager ProgID is currently advertised under at least
    /// one of the supported extensions' <c>OpenWithProgids</c> key. We use
    /// the .ttf entry as a sentinel because Register/Unregister are atomic
    /// over the full set.
    /// </summary>
    public static bool IsRegistered()
    {
        try
        {
            using var openWith = Registry.CurrentUser.OpenSubKey(
                @"Software\Classes\.ttf\OpenWithProgids", false);
            return openWith?.GetValue(ProgId) is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Adds Fontager to the "Open with..." list for every extension in
    /// <see cref="SupportedExtensions"/> for the current user, and registers
    /// the application under <c>HKCU\Software\Classes\Applications</c>.
    /// Returns true on success. No-op (returns false) when running packaged
    /// because the writes would be virtualized.
    /// </summary>
    public static bool RegisterForCurrentUser()
    {
        if (IsRunningPackaged) return false;

        var exePath = GetExecutablePath();
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return false;

        var openCommand = $"\"{exePath}\" \"%1\"";
        var iconRef = $"\"{exePath}\",0";

        // Migrate away from the legacy single-extension ProgID if it's still
        // sitting in the registry from a previous Fontager install — we
        // don't want two ProgIDs both claiming the same EXE.
        RemoveLegacyTtfProgId();
        RemoveLegacyApplicationRegistration();

        // 1. ProgID definition: HKCU\Software\Classes\Fontager.Viewer.font
        using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}", true))
        {
            progIdKey?.SetValue(string.Empty, "Font file (Fontager)");
            using var iconKey = progIdKey?.CreateSubKey("DefaultIcon", true);
            iconKey?.SetValue(string.Empty, iconRef);
            using var cmdKey = progIdKey?.CreateSubKey(@"shell\open\command", true);
            cmdKey?.SetValue(string.Empty, openCommand);
        }

        // 2. Application registration: HKCU\Software\Classes\Applications\<exe name>
        //    Tells Windows which file types Fontager understands, so it shows
        //    up in "Open with → Choose another app" lists even before the
        //    user has explicitly associated anything.
        using (var appKey = Registry.CurrentUser.CreateSubKey(
            $@"Software\Classes\Applications\{AppExeFileName}", true))
        {
            appKey?.SetValue("FriendlyAppName", "Fontager Viewer");
            using var cmdKey = appKey?.CreateSubKey(@"shell\open\command", true);
            cmdKey?.SetValue(string.Empty, openCommand);
            using var supportedKey = appKey?.CreateSubKey("SupportedTypes", true);
            foreach (var ext in SupportedExtensions)
                supportedKey?.SetValue(ext, string.Empty);
        }

        // 3. Per-extension OpenWithProgids entry. Windows uses this to
        //    populate the "Open with..." submenu.
        foreach (var ext in SupportedExtensions)
        {
            using var openWith = Registry.CurrentUser.CreateSubKey(
                $@"Software\Classes\{ext}\OpenWithProgids", true);
            openWith?.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        NotifyShellAssociationChanged();
        return true;
    }

    /// <summary>
    /// Reverses <see cref="RegisterForCurrentUser"/>. Only deletes entries we
    /// own; never touches other applications or HKLM. Also removes any
    /// stale legacy <c>Fontager.Viewer.ttf</c> ProgID from older versions.
    /// </summary>
    public static bool UnregisterForCurrentUser()
    {
        if (IsRunningPackaged) return false;

        try
        {
            foreach (var ext in SupportedExtensions)
            {
                using var openWith = Registry.CurrentUser.OpenSubKey(
                    $@"Software\Classes\{ext}\OpenWithProgids", true);
                if (openWith?.GetValue(ProgId) is not null)
                    openWith.DeleteValue(ProgId, throwOnMissingValue: false);
            }

            Registry.CurrentUser.DeleteSubKeyTree(
                $@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(
                $@"Software\Classes\Applications\{AppExeFileName}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(
                $@"Software\Classes\Applications\{LegacyApplicationExeName}", throwOnMissingSubKey: false);

            RemoveLegacyTtfProgId();

            NotifyShellAssociationChanged();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Removes the older <c>Fontager.Viewer.ttf</c> ProgID and its OpenWith
    /// pointer from <c>.ttf</c>. Safe to call when the entries don't exist.
    /// </summary>
    private static void RemoveLegacyTtfProgId()
    {
        try
        {
            using (var openWith = Registry.CurrentUser.OpenSubKey(
                @"Software\Classes\.ttf\OpenWithProgids", true))
            {
                if (openWith?.GetValue(LegacyTtfProgId) is not null)
                    openWith.DeleteValue(LegacyTtfProgId, throwOnMissingValue: false);
            }
            Registry.CurrentUser.DeleteSubKeyTree(
                $@"Software\Classes\{LegacyTtfProgId}", throwOnMissingSubKey: false);
        }
        catch
        {
            // Best-effort cleanup — never block the main flow on this.
        }
    }

    private static string GetExecutablePath()
    {
        var module = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrEmpty(module) && File.Exists(module)) return module;

        return Path.Combine(AppContext.BaseDirectory, AppExeFileName);
    }

    /// <summary>
    /// Removes <c>Applications\Fontager.Viewer.exe</c> left from builds before the host was renamed.
    /// </summary>
    private static void RemoveLegacyApplicationRegistration()
    {
        try
        {
            if (string.Equals(AppExeFileName, LegacyApplicationExeName, StringComparison.OrdinalIgnoreCase))
                return;
            Registry.CurrentUser.DeleteSubKeyTree(
                $@"Software\Classes\Applications\{LegacyApplicationExeName}", throwOnMissingSubKey: false);
        }
        catch
        {
            // Best-effort
        }
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
