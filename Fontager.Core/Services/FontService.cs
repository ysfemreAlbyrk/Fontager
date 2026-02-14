using Fontager.Core.Helpers;
using Fontager.Core.Models;

namespace Fontager.Core.Services;

/// <summary>
/// Default implementation of <see cref="IFontService"/>.
/// </summary>
public sealed class FontService : IFontService
{
    private static readonly string[] _supportedExtensions = [".ttf", ".otf", ".ttc", ".woff2"];

    public IReadOnlyList<string> SupportedExtensions => _supportedExtensions;

    public bool IsSupportedFont(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return _supportedExtensions.Contains(ext);
    }

    public Task<FontModel?> LoadFontAsync(string filePath) => LoadFontAsync(filePath, 0);

    public async Task<FontModel?> LoadFontAsync(string filePath, int fontIndex)
    {
        if (!IsSupportedFont(filePath))
            return null;

        if (!File.Exists(filePath))
            return null;

        try
        {
            var fileInfo = new FileInfo(filePath);
            var format = FontParser.GetFormatFromExtension(filePath);

            FontMetadata metadata;
            int fontCount = 1;

            if (format == FontFormat.WebOpenFont)
            {
                metadata = CreateMetadataFromFileName(filePath);
            }
            else
            {
                fontCount = await Task.Run(() => FontParser.GetFontCount(filePath));
                if (fontIndex < 0 || fontIndex >= fontCount)
                    fontIndex = 0;

                metadata = await Task.Run(() => FontParser.Parse(filePath, fontIndex));

                if (string.IsNullOrWhiteSpace(metadata.FamilyName))
                {
                    metadata = metadata with
                    {
                        FamilyName = CleanFontName(Path.GetFileNameWithoutExtension(filePath)),
                        FullName = CleanFontName(Path.GetFileNameWithoutExtension(filePath))
                    };
                }
            }

            return new FontModel
            {
                FilePath = filePath,
                FileSize = fileInfo.Length,
                Format = format,
                Metadata = metadata,
                FontCount = fontCount,
                FontIndex = fontIndex
            };
        }
        catch
        {
            return null;
        }
    }

    public int GetFontCount(string filePath)
    {
        if (!IsSupportedFont(filePath) || !File.Exists(filePath))
            return 1;

        var format = FontParser.GetFormatFromExtension(filePath);
        if (format == FontFormat.TrueTypeCollection)
            return FontParser.GetFontCount(filePath);

        return 1;
    }

    public async Task<IReadOnlyList<FontModel>> LoadFontsFromDirectoryAsync(string directoryPath, bool recursive = true)
    {
        if (!Directory.Exists(directoryPath))
            return [];

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var fonts = new List<FontModel>();

        var fontFiles = _supportedExtensions
            .SelectMany(ext => Directory.EnumerateFiles(directoryPath, $"*{ext}", searchOption))
            .ToList();

        var tasks = fontFiles.Select(f => LoadFontAsync(f));
        var results = await Task.WhenAll(tasks);

        foreach (var font in results)
        {
            if (font is not null)
                fonts.Add(font);
        }

        return fonts;
    }

    /// <summary>
    /// Creates basic metadata from a font file name when binary parsing is not possible.
    /// Handles patterns like "Inter-BoldItalic.woff2", "Roboto_Mono-Regular.woff2".
    /// </summary>
    private static FontMetadata CreateMetadataFromFileName(string filePath)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        var cleanName = CleanFontName(nameWithoutExt);

        // Try to split family and style on common separators
        string familyName = cleanName;
        string subfamilyName = "Regular";
        int weight = 400;
        bool isItalic = false;

        // Check for style suffix patterns: "Inter-Bold", "Roboto_Mono-BoldItalic"
        var dashIndex = nameWithoutExt.LastIndexOf('-');
        if (dashIndex > 0 && dashIndex < nameWithoutExt.Length - 1)
        {
            var possibleFamily = nameWithoutExt[..dashIndex];
            var possibleStyle = nameWithoutExt[(dashIndex + 1)..];

            familyName = CleanFontName(possibleFamily);
            subfamilyName = possibleStyle;

            // Parse weight from style name
            var styleLower = possibleStyle.ToLowerInvariant();
            weight = ParseWeightFromStyleName(styleLower);
            isItalic = styleLower.Contains("italic");
        }

        return new FontMetadata
        {
            FamilyName = familyName,
            SubfamilyName = subfamilyName,
            FullName = cleanName,
            Weight = weight,
            IsItalic = isItalic
        };
    }

    /// <summary>
    /// Cleans a font file name: replaces underscores/hyphens with spaces.
    /// </summary>
    private static string CleanFontName(string name)
    {
        return name.Replace('_', ' ').Replace('-', ' ').Trim();
    }

    /// <summary>
    /// Parses a font weight value from a style name string.
    /// </summary>
    private static int ParseWeightFromStyleName(string styleLower)
    {
        if (styleLower.Contains("thin") || styleLower.Contains("hairline")) return 100;
        if (styleLower.Contains("extralight") || styleLower.Contains("ultralight")) return 200;
        if (styleLower.Contains("light")) return 300;
        if (styleLower.Contains("medium")) return 500;
        if (styleLower.Contains("semibold") || styleLower.Contains("demibold")) return 600;
        if (styleLower.Contains("extrabold") || styleLower.Contains("ultrabold")) return 800;
        if (styleLower.Contains("bold")) return 700;
        if (styleLower.Contains("black") || styleLower.Contains("heavy")) return 900;
        return 400; // Regular
    }
}
