using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace Fontager.Viewer.Services;

/// <summary>
/// Machine-wide font install to <c>%Windir%\Fonts</c> + <c>HKLM\...\Fonts</c>.
/// Used from the main app when elevated and from a short-lived elevated helper process.
/// </summary>
internal static class FontInstallerService
{
    public const int ExitSuccess = 0;
    public const int ExitError = 1;
    public const int ExitAlreadyExists = 2;

    private const string FontsRegPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";
    private const uint HWND_BROADCAST = 0xffff;
    private const uint WM_FONTCHANGE = 0x001D;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    public static int InstallForAllUsers(string sourcePath, string displayName, bool overwrite)
    {
        try
        {
            if (!File.Exists(sourcePath))
                return ExitError;

            var fileName = Path.GetFileName(sourcePath);
            var systemFontsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            var destPath = Path.Combine(systemFontsDir, fileName);
            var registryValueName = BuildFontRegistryValueName(displayName, sourcePath);

            if (File.Exists(destPath) && !overwrite)
                return ExitAlreadyExists;

            InstallFontFileReplacing(sourcePath, destPath);

            using (var regKey = Registry.LocalMachine.OpenSubKey(FontsRegPath, writable: true))
            {
                regKey?.SetValue(registryValueName, fileName);
            }

            NotifyFontInstalled(destPath);
            return ExitSuccess;
        }
        catch
        {
            return ExitError;
        }
    }

    private static void InstallFontFileReplacing(string sourcePath, string destPath)
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
            CopyFontFileViaStaging(fullSrc, fullDst);
        }
    }

    private static void CopyFontFileViaStaging(string fullSrc, string fullDst)
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
        uint hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
}
