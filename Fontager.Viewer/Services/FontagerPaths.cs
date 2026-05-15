using System;
using System.IO;

namespace Fontager.Viewer.Services;

/// <summary>
/// Per-user app data under <c>%LocalAppData%\Fontager</c> (settings).
/// Font preview cache uses <see cref="FontCacheSetup"/> (install-relative for ms-appx).
/// </summary>
internal static class FontagerPaths
{
    public static string LocalAppDataRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Fontager");
}
