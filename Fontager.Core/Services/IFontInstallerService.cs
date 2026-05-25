using System.Threading.Tasks;

namespace Fontager.Core.Services;

/// <summary>
/// Target for the font installation.
/// </summary>
public enum FontInstallTarget
{
    CurrentUser = 0,
    AllUsers = 1
}

/// <summary>
/// Result of the font installation attempt.
/// </summary>
public enum FontInstallResult
{
    Success = 0,
    AlreadyExists = 1,
    NotSupported = 2,
    AccessDenied = 3,
    Failed = 4
}

/// <summary>
/// Provides font installation operations to the application.
/// </summary>
public interface IFontInstallerService
{
    /// <summary>
    /// Gets whether the current process is running with elevated administrator rights.
    /// </summary>
    bool IsElevated { get; }

    /// <summary>
    /// Installs a font to the specified target.
    /// </summary>
    Task<FontInstallResult> InstallFontAsync(string sourcePath, string fontDisplayName, FontInstallTarget target, bool overwrite);

    /// <summary>
    /// Performs a synchronous machine-wide installation. Used by the elevated one-shot helper.
    /// </summary>
    int InstallForAllUsersSynchronous(string sourcePath, string fontDisplayName, bool overwrite);
}
