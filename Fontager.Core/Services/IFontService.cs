using Fontager.Core.Models;

namespace Fontager.Core.Services;

/// <summary>
/// Service for loading and parsing font files.
/// </summary>
public interface IFontService
{
    /// <summary>
    /// Supported font file extensions.
    /// </summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>
    /// Checks if a file path points to a supported font file.
    /// </summary>
    bool IsSupportedFont(string filePath);

    /// <summary>
    /// Loads a font file and parses its metadata.
    /// </summary>
    Task<FontModel?> LoadFontAsync(string filePath);

    /// <summary>
    /// Loads all fonts from a directory.
    /// </summary>
    Task<IReadOnlyList<FontModel>> LoadFontsFromDirectoryAsync(string directoryPath, bool recursive = true);
}
