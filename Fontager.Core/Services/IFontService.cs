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
    /// Loads a font file and parses its metadata (first font if multi-font file).
    /// </summary>
    Task<FontModel?> LoadFontAsync(string filePath);

    /// <summary>
    /// Loads a specific font from a multi-font file (e.g. TTC) by index.
    /// </summary>
    Task<FontModel?> LoadFontAsync(string filePath, int fontIndex);

    /// <summary>
    /// Returns the number of fonts in a file (>1 for TTC collections).
    /// </summary>
    int GetFontCount(string filePath);

    /// <summary>
    /// Loads all fonts from a directory.
    /// </summary>
    Task<IReadOnlyList<FontModel>> LoadFontsFromDirectoryAsync(string directoryPath, bool recursive = true);
}
