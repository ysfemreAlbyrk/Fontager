namespace Fontager.Core.Models;

/// <summary>
/// Metadata extracted from a font file's name and OS/2 tables.
/// </summary>
public sealed record FontMetadata
{
    /// <summary>Font family name from name ID 1 (e.g. "Inter").</summary>
    public string FamilyName { get; init; } = string.Empty;

    /// <summary>Typographic/preferred family name from name ID 16 (e.g. "Material Icons").
    /// Falls back to FamilyName if not present.</summary>
    public string TypographicFamilyName { get; init; } = string.Empty;

    /// <summary>Font subfamily / style (e.g. "Bold Italic").</summary>
    public string SubfamilyName { get; init; } = string.Empty;

    /// <summary>Full font name (e.g. "Inter Bold Italic").</summary>
    public string FullName { get; init; } = string.Empty;

    /// <summary>PostScript name (e.g. "Inter-BoldItalic").</summary>
    public string PostScriptName { get; init; } = string.Empty;

    /// <summary>Font designer name.</summary>
    public string Designer { get; init; } = string.Empty;

    /// <summary>Font description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>License information.</summary>
    public string License { get; init; } = string.Empty;

    /// <summary>License URL.</summary>
    public string LicenseUrl { get; init; } = string.Empty;

    /// <summary>Font vendor / foundry.</summary>
    public string Vendor { get; init; } = string.Empty;

    /// <summary>Font version string (e.g. "Version 4.001").</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Copyright notice.</summary>
    public string Copyright { get; init; } = string.Empty;

    /// <summary>Trademark notice.</summary>
    public string Trademark { get; init; } = string.Empty;

    /// <summary>Number of glyphs in the font.</summary>
    public int GlyphCount { get; init; }

    /// <summary>Whether this is a variable font.</summary>
    public bool IsVariable { get; init; }

    /// <summary>Units per em.</summary>
    public int UnitsPerEm { get; init; }

    /// <summary>Font weight value (100-900).</summary>
    public int Weight { get; init; } = 400;

    /// <summary>Whether the font is italic.</summary>
    public bool IsItalic { get; init; }

    /// <summary>Whether the font is oblique.</summary>
    public bool IsOblique { get; init; }

    /// <summary>Font classification derived from OS/2 table.</summary>
    public FontClassification Classification { get; init; } = FontClassification.None;

    /// <summary>
    /// Set of Unicode code points the font's cmap actually maps to a glyph.
    /// Empty if the cmap could not be parsed (e.g. WOFF2 files that are not
    /// decompressed). Use this instead of hard-coded Unicode ranges to drive
    /// the glyph grid.
    /// </summary>
    public IReadOnlySet<int> SupportedCodePoints { get; init; } = new HashSet<int>();
}
