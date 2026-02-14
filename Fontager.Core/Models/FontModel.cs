namespace Fontager.Core.Models;

/// <summary>
/// Represents a single font file with its metadata and state.
/// </summary>
public sealed class FontModel
{
    /// <summary>Absolute path to the font file.</summary>
    public required string FilePath { get; init; }

    /// <summary>File name with extension (e.g. "Inter-Bold.ttf").</summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>File size in bytes.</summary>
    public long FileSize { get; init; }

    /// <summary>Font file format.</summary>
    public FontFormat Format { get; init; }

    /// <summary>Parsed metadata from the font file.</summary>
    public FontMetadata Metadata { get; init; } = new();

    /// <summary>Display name (prefers FullName, falls back to FileName).</summary>
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Metadata.FullName) ? Metadata.FullName : Path.GetFileNameWithoutExtension(FilePath);

    /// <summary>Formatted file size string.</summary>
    public string FormattedFileSize => FileSize switch
    {
        < 1024 => $"{FileSize} B",
        < 1024 * 1024 => $"{FileSize / 1024.0:F1} KB",
        _ => $"{FileSize / (1024.0 * 1024.0):F1} MB"
    };
}
