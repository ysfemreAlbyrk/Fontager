namespace Fontager.Core.Models;

/// <summary>
/// Metadata extracted from a font file's name / OS/2 / head / hhea / post /
/// fvar / GPOS / GSUB tables.
///
/// <para>
/// The shape mirrors what an OpenType font actually carries: identification
/// strings (name table), weight/style/classification flags (OS/2 + head),
/// design metrics (head + hhea + OS/2), embedding policy (OS/2.fsType),
/// variation axes (fvar), and the list of OpenType Layout features
/// (GSUB/GPOS). See <c>docs/research/font-metadata.md</c> for a per-table
/// reference and <c>docs/research/font-properties.md</c> for what each
/// property here means in practice.
/// </para>
/// </summary>
public sealed record FontMetadata
{
    // ── Identification (name table) ──────────────────────────────────

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

    /// <summary>Font designer name (name ID 9).</summary>
    public string Designer { get; init; } = string.Empty;

    /// <summary>Designer URL (name ID 12).</summary>
    public string DesignerUrl { get; init; } = string.Empty;

    /// <summary>Manufacturer name (name ID 8).</summary>
    public string Manufacturer { get; init; } = string.Empty;

    /// <summary>Manufacturer / vendor URL (name ID 11).</summary>
    public string ManufacturerUrl { get; init; } = string.Empty;

    /// <summary>Font description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Sample text the foundry suggests for previews (name ID 19).</summary>
    public string SampleText { get; init; } = string.Empty;

    /// <summary>License information.</summary>
    public string License { get; init; } = string.Empty;

    /// <summary>License URL.</summary>
    public string LicenseUrl { get; init; } = string.Empty;

    /// <summary>Font vendor / foundry (OS/2 achVendID).</summary>
    public string Vendor { get; init; } = string.Empty;

    /// <summary>Font version string (e.g. "Version 4.001").</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>head table fontRevision as a printable number (e.g. "1.234").</summary>
    public string FontRevision { get; init; } = string.Empty;

    /// <summary>Unique identifier string (name ID 3).</summary>
    public string UniqueId { get; init; } = string.Empty;

    /// <summary>Copyright notice.</summary>
    public string Copyright { get; init; } = string.Empty;

    /// <summary>Trademark notice.</summary>
    public string Trademark { get; init; } = string.Empty;

    // ── head / maxp ──────────────────────────────────────────────────

    /// <summary>Number of glyphs in the font (maxp.numGlyphs).</summary>
    public int GlyphCount { get; init; }

    /// <summary>Units per em (head.unitsPerEm). Typical values: 1000 for CFF, 1024/2048 for TrueType.</summary>
    public int UnitsPerEm { get; init; }

    /// <summary>head.xMin — bounding box of all glyphs in font units.</summary>
    public int XMin { get; init; }
    /// <summary>head.yMin.</summary>
    public int YMin { get; init; }
    /// <summary>head.xMax.</summary>
    public int XMax { get; init; }
    /// <summary>head.yMax.</summary>
    public int YMax { get; init; }

    /// <summary>head.created as a printable string (UTC).</summary>
    public string Created { get; init; } = string.Empty;
    /// <summary>head.modified as a printable string (UTC).</summary>
    public string Modified { get; init; } = string.Empty;

    /// <summary>head.macStyle bits decoded (Bold/Italic/Underline/Outline/Shadow/Condensed/Extended).</summary>
    public string MacStyle { get; init; } = string.Empty;

    // ── OS/2: style ──────────────────────────────────────────────────

    /// <summary>Whether this is a variable font (an fvar table is present).</summary>
    public bool IsVariable { get; init; }

    /// <summary>Font weight value (100-900). OS/2.usWeightClass.</summary>
    public int Weight { get; init; } = 400;

    /// <summary>OS/2.usWidthClass (1=Ultra-condensed … 9=Ultra-expanded).</summary>
    public int Width { get; init; } = 5;

    /// <summary>Whether the font is italic (OS/2.fsSelection bit 0 or macStyle italic).</summary>
    public bool IsItalic { get; init; }

    /// <summary>Whether the font is oblique (OS/2.fsSelection bit 9).</summary>
    public bool IsOblique { get; init; }

    /// <summary>Whether the post.isFixedPitch flag is set (monospaced metrics).</summary>
    public bool IsFixedPitch { get; init; }

    /// <summary>Font classification derived from OS/2 table.</summary>
    public FontClassification Classification { get; init; } = FontClassification.None;

    /// <summary>Decoded OS/2.panose bytes joined with hyphens; empty when the table is missing.</summary>
    public string Panose { get; init; } = string.Empty;

    /// <summary>OS/2.fsType embedding-permission bits decoded into a short summary
    /// (e.g. "Installable", "Editable", "Restricted").</summary>
    public string EmbeddingRights { get; init; } = string.Empty;

    /// <summary>Raw OS/2.fsType integer value.</summary>
    public int EmbeddingFlags { get; init; }

    // ── Vertical metrics (OS/2 + hhea) ───────────────────────────────

    /// <summary>OS/2.sTypoAscender (preferred ascender for typographic layout).</summary>
    public int TypoAscender { get; init; }
    /// <summary>OS/2.sTypoDescender (negative).</summary>
    public int TypoDescender { get; init; }
    /// <summary>OS/2.sTypoLineGap.</summary>
    public int TypoLineGap { get; init; }
    /// <summary>OS/2.usWinAscent — Windows clipping-box ascent.</summary>
    public int WinAscent { get; init; }
    /// <summary>OS/2.usWinDescent — Windows clipping-box descent (positive).</summary>
    public int WinDescent { get; init; }
    /// <summary>OS/2.sxHeight — height of 'x' in font units (0 when missing).</summary>
    public int XHeight { get; init; }
    /// <summary>OS/2.sCapHeight — cap height in font units (0 when missing).</summary>
    public int CapHeight { get; init; }

    /// <summary>hhea.ascender.</summary>
    public int HheaAscender { get; init; }
    /// <summary>hhea.descender (negative).</summary>
    public int HheaDescender { get; init; }
    /// <summary>hhea.lineGap.</summary>
    public int HheaLineGap { get; init; }

    /// <summary>post.italicAngle in degrees as a printable string ("-12.5"). Empty when post is missing.</summary>
    public string ItalicAngle { get; init; } = string.Empty;
    /// <summary>post.underlinePosition.</summary>
    public int UnderlinePosition { get; init; }
    /// <summary>post.underlineThickness.</summary>
    public int UnderlineThickness { get; init; }

    // ── Variable fonts (fvar) ────────────────────────────────────────

    /// <summary>List of declared variation axes (fvar.axes). Empty for non-variable fonts.</summary>
    public IReadOnlyList<VariationAxis> Axes { get; init; } = System.Array.Empty<VariationAxis>();

    // ── OpenType Layout (GSUB / GPOS) ───────────────────────────────

    /// <summary>4-character GSUB feature tags present in the font (e.g. "liga", "kern", "ss01").</summary>
    public IReadOnlyList<string> GsubFeatures { get; init; } = System.Array.Empty<string>();

    /// <summary>4-character GPOS feature tags present in the font (e.g. "kern", "mark", "size").</summary>
    public IReadOnlyList<string> GposFeatures { get; init; } = System.Array.Empty<string>();

    // ── Coverage (cmap) ──────────────────────────────────────────────

    /// <summary>
    /// Set of Unicode code points the font's cmap actually maps to a glyph.
    /// Empty if the cmap could not be parsed (e.g. WOFF2 files that are not
    /// decompressed). Use this instead of hard-coded Unicode ranges to drive
    /// the glyph grid.
    /// </summary>
    public IReadOnlySet<int> SupportedCodePoints { get; init; } = new HashSet<int>();
}

/// <summary>One axis declared in the fvar table of a variable font.</summary>
public sealed record VariationAxis(string Tag, string Name, double Min, double Default, double Max);
