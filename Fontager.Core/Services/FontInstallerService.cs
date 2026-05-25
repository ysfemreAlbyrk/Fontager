using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Fontager.Core.Helpers;

namespace Fontager.Core.Services;

/// <summary>
/// Service responsible for installing fonts to the current user (HKCU) or machine-wide (HKLM).
/// </summary>
public sealed class FontInstallerService : IFontInstallerService
{
    private const string FontsRegPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";
    
    // Win32 Message Broadcast Constants
    private const uint WM_FONTCHANGE = 0x001D;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    // Font registration flags
    private const uint FR_PRIVATE = 0x10;

    public bool IsElevated => ProcessElevationHelper.IsRunningElevated();

    /// <summary>
    /// Installs a font to the specified target.
    /// </summary>
    public async Task<FontInstallResult> InstallFontAsync(string sourcePath, string fontDisplayName, FontInstallTarget target, bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return FontInstallResult.Failed;

        if (target == FontInstallTarget.AllUsers)
        {
            if (!IsElevated)
            {
                // Delegation to elevated child process via UAC prompt
                var exitCode = await Task.Run(() => 
                    ProcessElevationHelper.TryInstallForAllUsersElevated(sourcePath, fontDisplayName, overwrite));

                return exitCode switch
                {
                    ProcessElevationHelper.ExitSuccess => FontInstallResult.Success,
                    ProcessElevationHelper.ExitAlreadyExists => FontInstallResult.AlreadyExists,
                    -1 => FontInstallResult.AccessDenied, // User cancelled UAC
                    _ => FontInstallResult.Failed
                };
            }

            // Elevated path: perform all-users install directly
            return await InstallForAllUsersInternalAsync(sourcePath, fontDisplayName, overwrite);
        }
        else
        {
            // Per-user installation
            return await InstallForCurrentUserInternalAsync(sourcePath, fontDisplayName, overwrite);
        }
    }

    /// <summary>
    /// Synchronous machine-wide install helper used by the elevated command-line worker.
    /// </summary>
    public int InstallForAllUsersSynchronous(string sourcePath, string fontDisplayName, bool overwrite)
    {
        try
        {
            if (!File.Exists(sourcePath))
                return ProcessElevationHelper.ExitError;

            var fileName = Path.GetFileName(sourcePath);
            var systemFontsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            var destPath = Path.Combine(systemFontsDir, fileName);
            var registryValueName = BuildFontRegistryValueName(fontDisplayName, sourcePath);

            if (File.Exists(destPath) && !overwrite)
                return ProcessElevationHelper.ExitAlreadyExists;

            InstallFontFileReplacingSync(sourcePath, destPath);

            using (var regKey = Registry.LocalMachine.OpenSubKey(FontsRegPath, writable: true))
            {
                regKey?.SetValue(registryValueName, fileName);
            }

            NotifyFontInstalled(destPath);
            return ProcessElevationHelper.ExitSuccess;
        }
        catch
        {
            return ProcessElevationHelper.ExitError;
        }
    }

    private async Task<FontInstallResult> InstallForAllUsersInternalAsync(string sourcePath, string fontDisplayName, bool overwrite)
    {
        try
        {
            var fileName = Path.GetFileName(sourcePath);
            var systemFontsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            var destPath = Path.Combine(systemFontsDir, fileName);
            var registryValueName = BuildFontRegistryValueName(fontDisplayName, sourcePath);

            if (File.Exists(destPath) && !overwrite)
                return FontInstallResult.AlreadyExists;

            await InstallFontFileReplacingAsync(sourcePath, destPath);

            using (var regKey = Registry.LocalMachine.OpenSubKey(FontsRegPath, true))
            {
                regKey?.SetValue(registryValueName, fileName);
            }

            NotifyFontInstalled(destPath);
            return FontInstallResult.Success;
        }
        catch (UnauthorizedAccessException)
        {
            return FontInstallResult.AccessDenied;
        }
        catch
        {
            return FontInstallResult.Failed;
        }
    }

    private async Task<FontInstallResult> InstallForCurrentUserInternalAsync(string sourcePath, string fontDisplayName, bool overwrite)
    {
        try
        {
            var fileName = Path.GetFileName(sourcePath);
            var userFontsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Windows", "Fonts");
            
            Directory.CreateDirectory(userFontsDir);
            var destPath = Path.Combine(userFontsDir, fileName);
            var registryValueName = BuildFontRegistryValueName(fontDisplayName, sourcePath);

            if (File.Exists(destPath) && !overwrite)
                return FontInstallResult.AlreadyExists;

            await InstallFontFileReplacingAsync(sourcePath, destPath);

            // HKCU per-user install: full absolute path (HKLM stores filename only).
            using (var regKey = Registry.CurrentUser.CreateSubKey(FontsRegPath, writable: true))
            {
                regKey?.SetValue(registryValueName, destPath);
            }

            // Verify the write reached the real hive (catch MSIX registry virtualization issues)
            bool persisted;
            using (var verifyKey = Registry.CurrentUser.OpenSubKey(FontsRegPath, false))
            {
                persisted = verifyKey?.GetValue(registryValueName) is string s
                    && string.Equals(s, destPath, StringComparison.OrdinalIgnoreCase);
            }

            if (!persisted)
            {
                return FontInstallResult.Failed; // Virtualization mismatch
            }

            NotifyFontInstalled(destPath);
            return FontInstallResult.Success;
        }
        catch (UnauthorizedAccessException)
        {
            return FontInstallResult.AccessDenied;
        }
        catch
        {
            return FontInstallResult.Failed;
        }
    }

    private async Task InstallFontFileReplacingAsync(string sourcePath, string destPath)
    {
        var fullSrc = Path.GetFullPath(sourcePath);
        var fullDst = Path.GetFullPath(destPath);

        if (string.Equals(fullSrc, fullDst, StringComparison.OrdinalIgnoreCase))
        {
            TryUnloadSessionLoadsOfFontFile(fullDst);
            BroadcastFontChange();
            await Task.Delay(200);
            return;
        }

        if (File.Exists(fullDst))
            TryUnloadSessionLoadsOfFontFile(fullDst);

        BroadcastFontChange();
        await Task.Delay(200);

        for (var attempt = 0; attempt < 15; attempt++)
        {
            try
            {
                if (File.Exists(fullDst))
                    File.Delete(fullDst);
                break;
            }
            catch (IOException)
            {
                await Task.Delay(120);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(120);
            }
        }

        try
        {
            File.Copy(fullSrc, fullDst, overwrite: false);
            return;
        }
        catch (IOException)
        {
            // Staging fallback
        }

        await CopyFontFileViaStagingAsync(fullSrc, fullDst);
    }

    private void InstallFontFileReplacingSync(string sourcePath, string destPath)
    {
        var fullSrc = Path.GetFullPath(sourcePath);
        var fullDst = Path.GetFullPath(destPath);

        if (File.Exists(fullDst))
            TryUnloadSessionLoadsOfFontFile(fullDst);

        BroadcastFontChange();
        Thread.Sleep(200);

        for (var attempt = 0; attempt < 15; attempt++)
        {
            try
            {
                if (File.Exists(fullDst))
                    File.Delete(fullDst);
                break;
            }
            catch (IOException)
            {
                Thread.Sleep(120);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(120);
            }
        }

        try
        {
            File.Copy(fullSrc, fullDst, overwrite: false);
            return;
        }
        catch (IOException)
        {
            CopyFontFileViaStagingSync(fullSrc, fullDst);
        }
    }

    private async Task CopyFontFileViaStagingAsync(string fullSrc, string fullDst)
    {
        var dir = Path.GetDirectoryName(fullDst)
            ?? throw new InvalidOperationException("Invalid font destination path.");
        var ext = Path.GetExtension(fullDst);
        var staging = Path.Combine(dir, $".fontager-{Guid.NewGuid():N}.tmp{ext}");
        try
        {
            File.Copy(fullSrc, staging, overwrite: true);

            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    if (File.Exists(fullDst))
                        File.Delete(fullDst);
                    break;
                }
                catch (IOException)
                {
                    await Task.Delay(120);
                }
                catch (UnauthorizedAccessException)
                {
                    await Task.Delay(120);
                }
            }

            File.Move(staging, fullDst, overwrite: true);
        }
        finally
        {
            if (File.Exists(staging))
            {
                try { File.Delete(staging); } catch { /* ignore */ }
            }
        }
    }

    private void CopyFontFileViaStagingSync(string fullSrc, string fullDst)
    {
        var dir = Path.GetDirectoryName(fullDst)
            ?? throw new InvalidOperationException("Invalid font destination path.");
        var ext = Path.GetExtension(fullDst);
        var staging = Path.Combine(dir, $".fontager-{Guid.NewGuid():N}.tmp{ext}");
        try
        {
            File.Copy(fullSrc, staging, overwrite: true);

            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    if (File.Exists(fullDst))
                        File.Delete(fullDst);
                    break;
                }
                catch (IOException)
                {
                    Thread.Sleep(120);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(120);
                }
            }

            File.Move(staging, fullDst, overwrite: true);
        }
        finally
        {
            if (File.Exists(staging))
            {
                try { File.Delete(staging); } catch { /* ignore */ }
            }
        }
    }

    private static void TryUnloadSessionLoadsOfFontFile(string fontFilePath)
    {
        if (string.IsNullOrWhiteSpace(fontFilePath))
            return;

        try
        {
            RemoveFontResourceEx(fontFilePath, 0, IntPtr.Zero);
        }
        catch { /* ignore */ }

        try
        {
            for (var n = 0; n < 32; n++)
            {
                if (RemoveFontResource(fontFilePath) == 0)
                    break;
            }
        }
        catch { /* ignore */ }
    }

    private static void NotifyFontInstalled(string destPath)
    {
        try
        {
            AddFontResource(destPath);
        }
        catch { /* ignore */ }

        BroadcastFontChange();
    }

    private static void BroadcastFontChange()
    {
        SendMessageTimeout(
            HWND_BROADCAST, WM_FONTCHANGE,
            IntPtr.Zero, IntPtr.Zero,
            SMTO_ABORTIFHUNG, 1000, out _);
    }

    private static string BuildFontRegistryValueName(string displayName, string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        string suffix = ext switch
        {
            ".ttf" or ".ttc" => " (TrueType)",
            ".otf" => " (OpenType)",
            _ => string.Empty
        };
        return string.IsNullOrEmpty(suffix) ? displayName : displayName + suffix;
    }

    // ── Win32 P/Invokes ────────────────────────────────────────────

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int AddFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RemoveFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int AddFontResource(string lpszFilename);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RemoveFontResource(string lpszFilename);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
}
