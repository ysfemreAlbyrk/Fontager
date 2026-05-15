using System;
using System.IO;
using System.Threading.Tasks;
using Fontager.Core.Models;
using Fontager.Viewer.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fontager.Viewer;

/// <summary>
/// Font-install pipeline for <see cref="MainWindow"/>.
///
/// <para>
/// The flow has two destinations:
/// </para>
/// <list type="bullet">
///   <item><description><b>Current user</b> — copies to
///     <c>%LocalAppData%\Microsoft\Windows\Fonts</c>, writes
///     <c>HKCU\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts</c> using the
///     absolute path as the value (HKLM convention is filename only,
///     HKCU wants the full path), and verifies the write actually reached
///     the real hive (under MSIX identity it can get virtualized away).</description></item>
///   <item><description><b>All users</b> — copies to
///     <c>%Windir%\Fonts</c>, writes <c>HKLM</c> with the filename, requires
///     elevation.</description></item>
/// </list>
///
/// <para>
/// In both cases we call <c>AddFontResource</c> and broadcast
/// <c>WM_FONTCHANGE</c> after the file is in place so the shell, Settings →
/// Fonts, and the Font Cache service refresh without needing a sign-out.
/// </para>
///
/// <para>
/// The replace path (re-installing an already-present font) is its own can
/// of worms: Font Cache and other apps hold handles to the file, so we
/// release session GDI loads, broadcast a font-change, wait briefly, retry
/// the delete, and fall back to staging-file-then-rename if a direct copy
/// loses the race.
/// </para>
/// </summary>
public sealed partial class MainWindow
{
    // ── Install ────────────────────────────────────────────────

    private Task NotifyInstallSuccessAsync(string title, string message) =>
        _settings.ExitAppAfterSuccessfulInstall
            ? ShowInstallSuccessThenExitAppAsync(title, message)
            : ShowSuccessDialogAsync(title, message);

    private async void InstallSplitButton_Click(SplitButton sender, SplitButtonClickEventArgs args)
    {
        await InstallFontAsync(GetSavedInstallTarget());
    }

    private async void InstallCurrentUserMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetSavedInstallTarget(InstallTarget.CurrentUser);
        await InstallFontAsync(InstallTarget.CurrentUser);
    }

    private async void InstallAllUsersMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetSavedInstallTarget(InstallTarget.AllUsers);
        await InstallFontAsync(InstallTarget.AllUsers);
    }

    private bool CanInstallForAllUsers =>
        _isProcessElevated || _settings.ElevateForAllUsersInstall;

    private InstallTarget GetSavedInstallTarget()
    {
        if (!CanInstallForAllUsers)
            return InstallTarget.CurrentUser;

        return _settings.InstallMode == (int)InstallTarget.AllUsers
            ? InstallTarget.AllUsers
            : InstallTarget.CurrentUser;
    }

    private void SetSavedInstallTarget(InstallTarget target)
    {
        if (target == InstallTarget.AllUsers && !CanInstallForAllUsers)
            target = InstallTarget.CurrentUser;

        _settings.InstallMode = (int)target;
        UpdateInstallButtonPresentation(GetSavedInstallTarget());
    }

    private void UpdateInstallButtonPresentation(InstallTarget target)
    {
        bool isAllUsers = target == InstallTarget.AllUsers;
        InstallButtonText.Text = isAllUsers ? "Install (All users)" : "Install";

        string tip;
        if (_isProcessElevated)
        {
            tip = isAllUsers
                ? "Install font for all users (Windows\\Fonts, machine-wide)"
                : "Install font for the current user only";
        }
        else if (isAllUsers && _settings.ElevateForAllUsersInstall)
        {
            tip = "Install to C:\\Windows\\Fonts for all users. Windows may show UAC once for this install only.";
        }
        else
        {
            tip = isAllUsers
                ? "Install font for all users (enable “UAC for all-users install” in Settings, or run the entire app as administrator)"
                : "Install font for the current user only";
        }

        ToolTipService.SetToolTip(InstallSplitButton, tip);
    }

    /// <summary>
    /// Greys out flyout actions that require elevation when this process is not elevated.
    /// </summary>
    private void ApplyInstallElevatedUi()
    {
        InstallAllUsersMenuFlyoutItem.IsEnabled = CanInstallForAllUsers;
    }

    private async Task InstallFontAsync(InstallTarget target)
    {
        if (_currentFilePath == null) return;

        if (_viewModel.CurrentFont?.Format == FontFormat.WebOpenFont)
        {
            await ShowInfoDialogAsync(
                "Installation not supported",
                "Windows cannot install WOFF2 fonts. Convert to TrueType (.ttf) or OpenType (.otf) to install the font.");
            return;
        }

        bool installSystem = target == InstallTarget.AllUsers;
        const string FontsRegPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";

        try
        {
            var fontDisplayName = _viewModel.CurrentFont?.DisplayName
                ?? Path.GetFileNameWithoutExtension(_currentFilePath);
            var fileName = Path.GetFileName(_currentFilePath);
            var registryValueName = BuildFontRegistryValueName(fontDisplayName, _currentFilePath);

            if (installSystem && !_isProcessElevated)
            {
                if (!_settings.ElevateForAllUsersInstall)
                {
                    await ShowInfoDialogAsync(
                        "Administrator required",
                        "Installing to C:\\Windows\\Fonts needs either “UAC for all-users install” in Settings (recommended), or “Run entire app as administrator”.");
                    return;
                }

                var systemFontsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
                var destPath = Path.Combine(systemFontsDir, fileName);
                var overwrite = false;

                if (File.Exists(destPath))
                {
                    var confirm = await ShowConfirmDialogAsync("Font Already Installed",
                        "This font is already installed system-wide. Overwrite?");
                    if (!confirm) return;
                    overwrite = true;
                }

                var exitCode = ProcessElevationHelper.TryInstallForAllUsersElevated(
                    _currentFilePath, fontDisplayName, overwrite);

                if (exitCode == -1)
                    return;

                if (exitCode == FontInstallerService.ExitAlreadyExists)
                {
                    await ShowInfoDialogAsync("Font Already Installed",
                        "This font is already installed system-wide.");
                    return;
                }

                if (exitCode != FontInstallerService.ExitSuccess)
                {
                    await ShowErrorDialogAsync("Installation failed",
                        "Could not install the font for all users. Try again or use Settings → Run entire app as administrator.");
                    return;
                }

                await NotifyInstallSuccessAsync("Font installed",
                    $"'{fontDisplayName}' has been installed for all users (C:\\Windows\\Fonts).");
                return;
            }

            if (installSystem)
            {
                var systemFontsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
                var destPath = Path.Combine(systemFontsDir, fileName);

                if (File.Exists(destPath))
                {
                    var confirm = await ShowConfirmDialogAsync("Font Already Installed",
                        "This font is already installed system-wide. Overwrite?");
                    if (!confirm) return;
                }

                await InstallFontFileReplacingAsync(_currentFilePath, destPath);

                using (var regKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(FontsRegPath, true))
                {
                    regKey?.SetValue(registryValueName, fileName);
                }

                NotifyFontInstalled(destPath);

                await NotifyInstallSuccessAsync("Font installed", $"'{fontDisplayName}' has been installed for all users.");
            }
            else
            {
                var userFontsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Windows", "Fonts");
                Directory.CreateDirectory(userFontsDir);

                var destPath = Path.Combine(userFontsDir, fileName);

                if (File.Exists(destPath))
                {
                    var confirm = await ShowConfirmDialogAsync("Font Already Installed",
                        "This font is already installed for the current user. Overwrite?");
                    if (!confirm) return;
                }

                await InstallFontFileReplacingAsync(_currentFilePath, destPath);

                // HKCU per-user install: full absolute path (HKLM stores filename only).
                using (var regKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(FontsRegPath, writable: true))
                {
                    regKey?.SetValue(registryValueName, destPath);
                }

                // Verify the write reached the real hive (MSIX identity can virtualize
                // HKCU writes into the package container, in which case Settings → Fonts
                // never picks them up).
                bool persisted;
                using (var verifyKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(FontsRegPath, false))
                {
                    persisted = verifyKey?.GetValue(registryValueName) is string s
                        && string.Equals(s, destPath, StringComparison.OrdinalIgnoreCase);
                }

                if (!persisted)
                {
                    await ShowWarningDialogAsync("Installation incomplete",
                        $"The font file was copied to:\n{destPath}\n\nbut the registry entry under HKCU could not be verified. This usually means the app is running with a virtualized registry (packaged identity). Run Fontager unpackaged or use 'Install for all users' instead.");
                    return;
                }

                NotifyFontInstalled(destPath);

                await NotifyInstallSuccessAsync("Font installed", $"'{fontDisplayName}' has been installed for the current user and is now visible in Settings → Fonts.");
            }
        }
        catch (UnauthorizedAccessException)
        {
            await ShowErrorDialogAsync("Installation failed",
                "Access denied. System-wide installation requires running the application as administrator.");
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync("Installation failed", $"Could not install font: {ex.Message}");
        }
    }

    /// <summary>
    /// Replaces <paramref name="destPath"/> with the bytes from <paramref name="sourcePath"/>.
    /// Releases session GDI loads and broadcasts <c>WM_FONTCHANGE</c> so Windows Font Cache
    /// and other processes can close handles to an existing per-user/system font file before
    /// we delete or overwrite it (avoids "file is being used by another process" on reinstall).
    /// </summary>
    private async Task InstallFontFileReplacingAsync(string sourcePath, string destPath)
    {
        var fullSrc = Path.GetFullPath(sourcePath);
        var fullDst = Path.GetFullPath(destPath);

        // Previewing from the installed path: private GDI mapping locks the destination file.
        if (string.Equals(fullSrc, fullDst, StringComparison.OrdinalIgnoreCase))
        {
            DeactivateCurrentFont();
            TryUnloadSessionLoadsOfFontFile(fullDst);
            BroadcastFontChange();
            await Task.Delay(200);
            return;
        }

        if (_activeFontPath is not null
            && string.Equals(Path.GetFullPath(_activeFontPath), fullDst, StringComparison.OrdinalIgnoreCase))
        {
            DeactivateCurrentFont();
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
            // FontCache may still hold the destination briefly — try atomic-ish replace via staging.
        }

        await CopyFontFileViaStagingAsync(fullSrc, fullDst);
    }

    private static void TryUnloadSessionLoadsOfFontFile(string fontFilePath)
    {
        if (string.IsNullOrWhiteSpace(fontFilePath))
            return;

        try
        {
            RemoveFontResourceEx(fontFilePath, FR_PRIVATE, IntPtr.Zero);
        }
        catch
        {
            // ignore
        }

        try
        {
            for (var n = 0; n < 32; n++)
            {
                if (RemoveFontResource(fontFilePath) == 0)
                    break;
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void BroadcastFontChange()
    {
        SendMessageTimeout(
            HWND_BROADCAST, WM_FONTCHANGE,
            IntPtr.Zero, IntPtr.Zero,
            SMTO_ABORTIFHUNG, 1000, out _);
    }

    private static async Task CopyFontFileViaStagingAsync(string fullSrc, string fullDst)
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

    /// <summary>
    /// Makes a freshly-installed font visible to every running application:
    /// loads it into the process group via <c>AddFontResource</c> so the font
    /// is usable in this session, and broadcasts <c>WM_FONTCHANGE</c> so the
    /// shell, Settings → Fonts, and the font cache service refresh without a
    /// logoff.
    /// </summary>
    private static void NotifyFontInstalled(string destPath)
    {
        try
        {
            AddFontResource(destPath);
        }
        catch
        {
            // Best-effort; the registry entry alone is enough for next-session use.
        }

        BroadcastFontChange();
    }

    /// <summary>
    /// Builds the registry value name Windows expects. The convention is
    /// "{Family Name} (TrueType)" or "{Family Name} (OpenType)" depending on
    /// the file format. Without the suffix, Settings → Fonts may show the
    /// entry but the Font Cache service can refuse to register it.
    /// </summary>
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
}
