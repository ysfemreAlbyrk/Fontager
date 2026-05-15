using Fontager.Core.Models;

namespace Fontager.Core.Helpers;

/// <summary>
/// Parses TTF/OTF font files to extract metadata from name and OS/2 tables.
/// Reads raw binary data without any external dependencies.
/// </summary>
public static class FontParser
{
    // Name table Name IDs (see docs/research/font-metadata.md for the
    // full list and how the platform/encoding/language scoring works).
    private const int NameId_Copyright = 0;
    private const int NameId_FontFamily = 1;
    private const int NameId_FontSubfamily = 2;
    private const int NameId_UniqueId = 3;
    private const int NameId_FullFontName = 4;
    private const int NameId_Version = 5;
    private const int NameId_PostScriptName = 6;
    private const int NameId_Trademark = 7;
    private const int NameId_Manufacturer = 8;
    private const int NameId_Designer = 9;
    private const int NameId_Description = 10;
    private const int NameId_ManufacturerUrl = 11;
    private const int NameId_DesignerUrl = 12;
    private const int NameId_LicenseDescription = 13;
    private const int NameId_LicenseUrl = 14;
    private const int NameId_TypographicFamily = 16;
    private const int NameId_TypographicSubfamily = 17;
    private const int NameId_SampleText = 19;

    /// <summary>
    /// Determines the font format from the file extension.
    /// </summary>
    public static FontFormat GetFormatFromExtension(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".ttf" => FontFormat.TrueType,
            ".otf" => FontFormat.OpenType,
            ".ttc" => FontFormat.TrueTypeCollection,
            ".woff2" => FontFormat.WebOpenFont,
            _ => FontFormat.Unknown
        };
    }

    /// <summary>
    /// Returns the number of fonts contained in a font file.
    /// TTC files can contain multiple fonts; TTF/OTF always contain 1.
    /// </summary>
    public static int GetFontCount(string filePath)
    {
        try
        {
            var data = File.ReadAllBytes(filePath);
            return GetFontCount(data);
        }
        catch
        {
            return 1;
        }
    }

    /// <summary>
    /// Returns the number of fonts in the binary data.
    /// </summary>
    public static int GetFontCount(byte[] data)
    {
        if (data.Length < 12) return 1;

        var sfVersion = ReadUInt32BE(data, 0);
        if (sfVersion == 0x74746366) // 'ttcf'
        {
            if (data.Length < 12) return 1;
            return (int)ReadUInt32BE(data, 8); // numFonts
        }
        return 1;
    }

    /// <summary>
    /// Parses a font file and extracts metadata for the font at the given index.
    /// </summary>
    public static FontMetadata Parse(string filePath, int fontIndex = 0)
    {
        try
        {
            var data = File.ReadAllBytes(filePath);
            return Parse(data, fontIndex);
        }
        catch
        {
            return new FontMetadata();
        }
    }

    /// <summary>
    /// Parses all fonts in a file and returns a list of metadata.
    /// </summary>
    public static List<FontMetadata> ParseAll(string filePath)
    {
        try
        {
            var data = File.ReadAllBytes(filePath);
            var count = GetFontCount(data);
            var results = new List<FontMetadata>(count);
            for (int i = 0; i < count; i++)
            {
                results.Add(Parse(data, i));
            }
            return results;
        }
        catch
        {
            return [new FontMetadata()];
        }
    }

    /// <summary>
    /// Parses font binary data and extracts metadata for the font at the given index.
    /// </summary>
    public static FontMetadata Parse(byte[] data, int fontIndex = 0)
    {
        if (data.Length < 12)
            return new FontMetadata();

        try
        {
            var sfVersion = ReadUInt32BE(data, 0);

            int tableOffset = 0;
            if (sfVersion == 0x74746366) // 'ttcf'
            {
                if (data.Length < 12)
                    return new FontMetadata();
                var numFonts = (int)ReadUInt32BE(data, 8);
                if (fontIndex < 0 || fontIndex >= numFonts)
                    fontIndex = 0;
                int offsetPos = 12 + (fontIndex * 4);
                if (offsetPos + 4 > data.Length)
                    return new FontMetadata();
                tableOffset = (int)ReadUInt32BE(data, offsetPos);
            }

            var numTables = ReadUInt16BE(data, tableOffset + 4);

            int nameTableOffset = -1;
            int nameTableLength = 0;
            int os2TableOffset = -1;
            int os2TableLength = 0;
            int headTableOffset = -1;
            int hheaTableOffset = -1;
            int postTableOffset = -1;
            int maxpTableOffset = -1;
            int fvarTableOffset = -1;
            int fvarTableLength = 0;
            int gsubTableOffset = -1;
            int gposTableOffset = -1;
            int cmapTableOffset = -1;
            int cmapTableLength = 0;

            for (int i = 0; i < numTables; i++)
            {
                int entryOffset = tableOffset + 12 + (i * 16);
                if (entryOffset + 16 > data.Length) break;

                var tag = ReadTag(data, entryOffset);
                var offset = (int)ReadUInt32BE(data, entryOffset + 8);
                var length = (int)ReadUInt32BE(data, entryOffset + 12);

                switch (tag)
                {
                    case "name":
                        nameTableOffset = offset;
                        nameTableLength = length;
                        break;
                    case "OS/2":
                        os2TableOffset = offset;
                        os2TableLength = length;
                        break;
                    case "head":
                        headTableOffset = offset;
                        break;
                    case "hhea":
                        hheaTableOffset = offset;
                        break;
                    case "post":
                        postTableOffset = offset;
                        break;
                    case "maxp":
                        maxpTableOffset = offset;
                        break;
                    case "fvar":
                        fvarTableOffset = offset;
                        fvarTableLength = length;
                        break;
                    case "GSUB":
                        gsubTableOffset = offset;
                        break;
                    case "GPOS":
                        gposTableOffset = offset;
                        break;
                    case "cmap":
                        cmapTableOffset = offset;
                        cmapTableLength = length;
                        break;
                }
            }

            // Parse name table
            var names = new Dictionary<int, string>();
            if (nameTableOffset >= 0 && nameTableOffset + 6 <= data.Length)
            {
                names = ParseNameTable(data, nameTableOffset, nameTableLength);
            }

            // ── OS/2 ──────────────────────────────────────────────
            int weight = 400;
            int width = 5;
            bool isItalic = false;
            bool isOblique = false;
            FontClassification classification = FontClassification.None;
            string vendor = string.Empty;
            string panose = string.Empty;
            int fsType = 0;
            string embedRights = string.Empty;
            int typoAscender = 0, typoDescender = 0, typoLineGap = 0;
            int winAscent = 0, winDescent = 0;
            int xHeight = 0, capHeight = 0;

            if (os2TableOffset >= 0 && os2TableOffset + 10 <= data.Length)
            {
                weight = ReadUInt16BE(data, os2TableOffset + 4);
                if (weight < 100) weight = 100;
                if (weight > 1000) weight = 1000;

                if (os2TableOffset + 8 <= data.Length)
                {
                    width = ReadUInt16BE(data, os2TableOffset + 6);
                    if (width < 1) width = 1;
                    if (width > 9) width = 9;

                    fsType = ReadUInt16BE(data, os2TableOffset + 8);
                    embedRights = DecodeFsType(fsType);
                }

                if (os2TableOffset + 62 <= data.Length)
                {
                    var fsSelection = ReadUInt16BE(data, os2TableOffset + 62);
                    isItalic = (fsSelection & 0x01) != 0;
                    isOblique = (fsSelection & 0x200) != 0;
                }

                // Panose classification (offset 32-41 = 10 bytes)
                if (os2TableOffset + 42 <= data.Length)
                {
                    var panoseFamily = data[os2TableOffset + 32];
                    classification = panoseFamily switch
                    {
                        2 => FontClassification.Serif,
                        3 => FontClassification.Script,
                        4 => FontClassification.Display,
                        5 => FontClassification.Symbol,
                        _ => FontClassification.None
                    };
                    var panoseBytes = new int[10];
                    for (int j = 0; j < 10; j++)
                        panoseBytes[j] = data[os2TableOffset + 32 + j];
                    panose = string.Join('-', panoseBytes);
                }

                // achVendID (offset 58-61)
                if (os2TableOffset + 62 <= data.Length)
                {
                    vendor = ReadTag(data, os2TableOffset + 58);
                }

                // Vertical metrics (offsets 68/70/72 = sTypoAscender/Descender/LineGap).
                if (os2TableOffset + 78 <= data.Length)
                {
                    typoAscender = ReadInt16BE(data, os2TableOffset + 68);
                    typoDescender = ReadInt16BE(data, os2TableOffset + 70);
                    typoLineGap = ReadInt16BE(data, os2TableOffset + 72);
                    winAscent = ReadUInt16BE(data, os2TableOffset + 74);
                    winDescent = ReadUInt16BE(data, os2TableOffset + 76);
                }

                // sxHeight + sCapHeight live at offsets 86/88 in OS/2 v2+
                // (version is at offset 0). For v0/v1 the table stops earlier;
                // guarding on table length keeps us safe.
                if (os2TableOffset + 90 <= data.Length)
                {
                    xHeight = ReadInt16BE(data, os2TableOffset + 86);
                    capHeight = ReadInt16BE(data, os2TableOffset + 88);
                }
            }

            // ── head ──────────────────────────────────────────────
            int unitsPerEm = 0;
            int xMin = 0, yMin = 0, xMax = 0, yMax = 0;
            string created = string.Empty, modified = string.Empty;
            string macStyle = string.Empty;
            string fontRevision = string.Empty;
            if (headTableOffset >= 0 && headTableOffset + 54 <= data.Length)
            {
                fontRevision = ReadFixed1616(data, headTableOffset + 4).ToString("F3");
                created = ReadLongDateTime(data, headTableOffset + 20);
                modified = ReadLongDateTime(data, headTableOffset + 28);
                xMin = ReadInt16BE(data, headTableOffset + 36);
                yMin = ReadInt16BE(data, headTableOffset + 38);
                xMax = ReadInt16BE(data, headTableOffset + 40);
                yMax = ReadInt16BE(data, headTableOffset + 42);
                macStyle = DecodeMacStyle(ReadUInt16BE(data, headTableOffset + 44));
                unitsPerEm = ReadUInt16BE(data, headTableOffset + 18);
            }
            else if (headTableOffset >= 0 && headTableOffset + 20 <= data.Length)
            {
                // Truncated head — at least pick up unitsPerEm so size math works.
                unitsPerEm = ReadUInt16BE(data, headTableOffset + 18);
            }

            // ── hhea ──────────────────────────────────────────────
            int hheaAscender = 0, hheaDescender = 0, hheaLineGap = 0;
            if (hheaTableOffset >= 0 && hheaTableOffset + 8 <= data.Length)
            {
                hheaAscender = ReadInt16BE(data, hheaTableOffset + 4);
                hheaDescender = ReadInt16BE(data, hheaTableOffset + 6);
                hheaLineGap = ReadInt16BE(data, hheaTableOffset + 8);
            }

            // ── post ──────────────────────────────────────────────
            string italicAngle = string.Empty;
            int underlinePosition = 0, underlineThickness = 0;
            bool isFixedPitch = false;
            if (postTableOffset >= 0 && postTableOffset + 32 <= data.Length)
            {
                italicAngle = ReadFixed1616(data, postTableOffset + 4).ToString("F2");
                underlinePosition = ReadInt16BE(data, postTableOffset + 8);
                underlineThickness = ReadInt16BE(data, postTableOffset + 10);
                isFixedPitch = ReadUInt32BE(data, postTableOffset + 12) != 0;
            }

            // ── maxp ──────────────────────────────────────────────
            int glyphCount = 0;
            if (maxpTableOffset >= 0 && maxpTableOffset + 6 <= data.Length)
            {
                glyphCount = ReadUInt16BE(data, maxpTableOffset + 4);
            }

            // ── fvar (variable fonts) ─────────────────────────────
            bool isVariable = fvarTableOffset >= 0;
            List<VariationAxis> axes = new();
            Dictionary<int, string> tempNamesForAxes = new();

            // Refine classification using subfamily name
            var subfamilyLower = names.GetValueOrDefault(NameId_FontSubfamily, "").ToLowerInvariant();
            var familyLower = names.GetValueOrDefault(NameId_FontFamily, "").ToLowerInvariant();

            if (classification == FontClassification.None)
            {
                if (familyLower.Contains("mono") || familyLower.Contains("code") || familyLower.Contains("console"))
                    classification = FontClassification.Monospace;
                else if (familyLower.Contains("sans"))
                    classification = FontClassification.SansSerif;
                else if (familyLower.Contains("serif"))
                    classification = FontClassification.Serif;
                else if (familyLower.Contains("display") || familyLower.Contains("decorat"))
                    classification = FontClassification.Display;
                else if (familyLower.Contains("script") || familyLower.Contains("hand") || familyLower.Contains("cursive"))
                    classification = FontClassification.Script;
            }

            // Prefer typographic family (name ID 16) over basic family (name ID 1)
            // for XAML FontFamily resolution. Many fonts (e.g. Material Icons, Noto)
            // use name ID 16 as the canonical family name that DirectWrite expects.
            var basicFamily = names.GetValueOrDefault(NameId_FontFamily, string.Empty);
            var typoFamily = names.GetValueOrDefault(NameId_TypographicFamily, string.Empty);

            IReadOnlySet<int> supportedCodePoints = cmapTableOffset >= 0
                ? ParseCmapTable(data, cmapTableOffset, cmapTableLength)
                : new HashSet<int>();

            // fvar parsing happens after the name table is available because
            // each axis carries a nameID we resolve into a human-readable
            // axis name.
            if (fvarTableOffset >= 0 && fvarTableLength > 0)
            {
                axes = ParseFvarTable(data, fvarTableOffset, fvarTableLength, names);
            }

            var gsubFeatures = gsubTableOffset >= 0
                ? ParseLayoutFeatureTags(data, gsubTableOffset)
                : Array.Empty<string>();
            var gposFeatures = gposTableOffset >= 0
                ? ParseLayoutFeatureTags(data, gposTableOffset)
                : Array.Empty<string>();

            return new FontMetadata
            {
                FamilyName = basicFamily,
                TypographicFamilyName = !string.IsNullOrWhiteSpace(typoFamily) ? typoFamily : basicFamily,
                SubfamilyName = names.GetValueOrDefault(NameId_TypographicSubfamily,
                    names.GetValueOrDefault(NameId_FontSubfamily, string.Empty)),
                FullName = names.GetValueOrDefault(NameId_FullFontName, string.Empty),
                PostScriptName = names.GetValueOrDefault(NameId_PostScriptName, string.Empty),
                Designer = names.GetValueOrDefault(NameId_Designer, string.Empty),
                DesignerUrl = names.GetValueOrDefault(NameId_DesignerUrl, string.Empty),
                Manufacturer = names.GetValueOrDefault(NameId_Manufacturer, string.Empty),
                ManufacturerUrl = names.GetValueOrDefault(NameId_ManufacturerUrl, string.Empty),
                Description = names.GetValueOrDefault(NameId_Description, string.Empty),
                SampleText = names.GetValueOrDefault(NameId_SampleText, string.Empty),
                License = names.GetValueOrDefault(NameId_LicenseDescription, string.Empty),
                LicenseUrl = names.GetValueOrDefault(NameId_LicenseUrl, string.Empty),
                Vendor = vendor.Trim('\0', ' '),
                Version = names.GetValueOrDefault(NameId_Version, string.Empty),
                FontRevision = fontRevision,
                UniqueId = names.GetValueOrDefault(NameId_UniqueId, string.Empty),
                Copyright = names.GetValueOrDefault(NameId_Copyright, string.Empty),
                Trademark = names.GetValueOrDefault(NameId_Trademark, string.Empty),
                GlyphCount = glyphCount,
                IsVariable = isVariable,
                UnitsPerEm = unitsPerEm,
                XMin = xMin, YMin = yMin, XMax = xMax, YMax = yMax,
                Created = created,
                Modified = modified,
                MacStyle = macStyle,
                Weight = weight,
                Width = width,
                IsItalic = isItalic,
                IsOblique = isOblique,
                IsFixedPitch = isFixedPitch,
                Classification = classification,
                Panose = panose,
                EmbeddingRights = embedRights,
                EmbeddingFlags = fsType,
                TypoAscender = typoAscender,
                TypoDescender = typoDescender,
                TypoLineGap = typoLineGap,
                WinAscent = winAscent,
                WinDescent = winDescent,
                XHeight = xHeight,
                CapHeight = capHeight,
                HheaAscender = hheaAscender,
                HheaDescender = hheaDescender,
                HheaLineGap = hheaLineGap,
                ItalicAngle = italicAngle,
                UnderlinePosition = underlinePosition,
                UnderlineThickness = underlineThickness,
                Axes = axes,
                GsubFeatures = gsubFeatures,
                GposFeatures = gposFeatures,
                SupportedCodePoints = supportedCodePoints
            };
        }
        catch
        {
            return new FontMetadata();
        }
    }

    // ── fvar (variation axes) ─────────────────────────────────────────

    /// <summary>
    /// Parses an fvar table and returns its axis records.
    ///
    /// fvar layout (https://learn.microsoft.com/en-us/typography/opentype/spec/fvar):
    ///   uint16 majorVersion, uint16 minorVersion,
    ///   uint16 offsetToAxesArray, uint16 (reserved),
    ///   uint16 axisCount, uint16 axisSize (always 20 in v1),
    ///   uint16 instanceCount, uint16 instanceSize.
    /// Each VariationAxisRecord is 20 bytes:
    ///   tag(4) + minValue(Fixed) + defaultValue(Fixed) + maxValue(Fixed)
    ///   + flags(uint16) + axisNameID(uint16).
    /// </summary>
    private static List<VariationAxis> ParseFvarTable(
        byte[] data, int tableOffset, int tableLength, Dictionary<int, string> names)
    {
        var result = new List<VariationAxis>();
        if (tableOffset + 16 > data.Length) return result;

        int offsetToAxes = ReadUInt16BE(data, tableOffset + 4);
        int axisCount = ReadUInt16BE(data, tableOffset + 8);
        int axisSize = ReadUInt16BE(data, tableOffset + 10);
        if (axisSize < 20) return result; // malformed; bail rather than guess

        int p = tableOffset + offsetToAxes;
        for (int i = 0; i < axisCount; i++)
        {
            if (p + 20 > data.Length || p - tableOffset > tableLength) break;

            string tag = ReadTag(data, p);
            double min = ReadFixed1616(data, p + 4);
            double def = ReadFixed1616(data, p + 8);
            double max = ReadFixed1616(data, p + 12);
            int nameId = ReadUInt16BE(data, p + 18);
            string name = names.GetValueOrDefault(nameId, tag);

            result.Add(new VariationAxis(tag, name, min, def, max));
            p += axisSize;
        }
        return result;
    }

    // ── GSUB / GPOS (OpenType Layout feature tags) ────────────────────

    /// <summary>
    /// Returns the distinct 4-character feature tags declared by a GSUB or
    /// GPOS table. Walks the FeatureList only — we don't resolve scripts or
    /// langSys here; the goal is "what features does this font advertise".
    /// </summary>
    private static IReadOnlyList<string> ParseLayoutFeatureTags(byte[] data, int tableOffset)
    {
        // GSUB / GPOS header is identical: uint16 major, uint16 minor, then
        // three offsets (scriptList, featureList, lookupList) and in v1.1 a
        // featureVariationsOffset (uint32).
        if (tableOffset + 10 > data.Length) return Array.Empty<string>();

        int featureListOffset = tableOffset + ReadUInt16BE(data, tableOffset + 6);
        if (featureListOffset + 2 > data.Length) return Array.Empty<string>();

        int featureCount = ReadUInt16BE(data, featureListOffset);
        var tags = new HashSet<string>(featureCount);
        for (int i = 0; i < featureCount; i++)
        {
            int recordOffset = featureListOffset + 2 + i * 6;
            if (recordOffset + 6 > data.Length) break;
            tags.Add(ReadTag(data, recordOffset));
        }

        var arr = new string[tags.Count];
        tags.CopyTo(arr);
        Array.Sort(arr, StringComparer.Ordinal);
        return arr;
    }

    /// <summary>
    /// Walks the cmap table, picks the best Unicode subtable available, and
    /// returns the set of supported Unicode code points.
    ///
    /// Preference order: (3, 10) Win UCS-4 → (0, 4|6) Unicode full → (3, 1)
    /// Win UCS-2 → (0, anything) Unicode BMP. Falls back to subtable format 0
    /// for fonts that only ship Mac Roman.
    /// </summary>
    private static IReadOnlySet<int> ParseCmapTable(byte[] data, int tableOffset, int tableLength)
    {
        var result = new HashSet<int>();
        if (tableOffset + 4 > data.Length) return result;

        int numTables = ReadUInt16BE(data, tableOffset + 2);
        if (numTables <= 0) return result;

        // Score each encoding record; higher score wins.
        int bestOffset = -1;
        int bestScore = -1;

        for (int i = 0; i < numTables; i++)
        {
            int recordOffset = tableOffset + 4 + (i * 8);
            if (recordOffset + 8 > data.Length) break;

            int platformId = ReadUInt16BE(data, recordOffset);
            int encodingId = ReadUInt16BE(data, recordOffset + 2);
            int subtableOffset = tableOffset + (int)ReadUInt32BE(data, recordOffset + 4);
            if (subtableOffset + 2 > data.Length) continue;

            int score = ScoreCmapEncoding(platformId, encodingId);
            if (score > bestScore)
            {
                bestScore = score;
                bestOffset = subtableOffset;
            }
        }

        if (bestOffset < 0 || bestOffset + 2 > data.Length) return result;

        int format = ReadUInt16BE(data, bestOffset);
        switch (format)
        {
            case 0: ParseCmapFormat0(data, bestOffset, result); break;
            case 4: ParseCmapFormat4(data, bestOffset, result); break;
            case 6: ParseCmapFormat6(data, bestOffset, result); break;
            case 12: ParseCmapFormat12(data, bestOffset, result); break;
        }

        return result;
    }

    private static int ScoreCmapEncoding(int platformId, int encodingId)
    {
        // Higher is better.
        return (platformId, encodingId) switch
        {
            (3, 10) => 100, // Windows, Unicode full repertoire (preferred for non-BMP)
            (0, 6) => 95,  // Unicode full, format 13 only
            (0, 4) => 90,  // Unicode 2.0+ full
            (3, 1) => 80,  // Windows, Unicode BMP
            (0, 3) => 70,  // Unicode 2.0+ BMP
            (0, 2) => 65,  // ISO 10646 1993 (deprecated)
            (0, 1) => 60,  // Unicode 1.1
            (0, 0) => 55,  // Unicode 1.0
            (1, 0) => 10,  // Mac Roman (last resort)
            _ => 0
        };
    }

    private static void ParseCmapFormat0(byte[] data, int offset, HashSet<int> result)
    {
        if (offset + 6 + 256 > data.Length) return;
        for (int i = 0; i < 256; i++)
        {
            if (data[offset + 6 + i] != 0)
                result.Add(i);
        }
    }

    private static void ParseCmapFormat4(byte[] data, int offset, HashSet<int> result)
    {
        if (offset + 14 > data.Length) return;
        int segCountX2 = ReadUInt16BE(data, offset + 6);
        int segCount = segCountX2 / 2;
        if (segCount <= 0) return;

        int endCodeOffset = offset + 14;
        int startCodeOffset = endCodeOffset + segCountX2 + 2; // skip reservedPad
        if (startCodeOffset + segCountX2 > data.Length) return;

        for (int i = 0; i < segCount; i++)
        {
            int endCode = ReadUInt16BE(data, endCodeOffset + i * 2);
            int startCode = ReadUInt16BE(data, startCodeOffset + i * 2);

            // The required final segment is startCode=endCode=0xFFFF; skip it.
            if (startCode == 0xFFFF && endCode == 0xFFFF) continue;
            if (endCode < startCode) continue;

            // Cap each segment so a single broken record can't allocate forever.
            int span = endCode - startCode + 1;
            if (span > 0x10000) span = 0x10000;
            for (int cp = startCode; cp < startCode + span; cp++)
            {
                result.Add(cp);
            }
        }
    }

    private static void ParseCmapFormat6(byte[] data, int offset, HashSet<int> result)
    {
        if (offset + 10 > data.Length) return;
        int firstCode = ReadUInt16BE(data, offset + 6);
        int entryCount = ReadUInt16BE(data, offset + 8);

        int glyphArrayOffset = offset + 10;
        if (glyphArrayOffset + entryCount * 2 > data.Length) return;

        for (int i = 0; i < entryCount; i++)
        {
            int glyph = ReadUInt16BE(data, glyphArrayOffset + i * 2);
            if (glyph != 0) result.Add(firstCode + i);
        }
    }

    private static void ParseCmapFormat12(byte[] data, int offset, HashSet<int> result)
    {
        if (offset + 16 > data.Length) return;
        int numGroups = (int)ReadUInt32BE(data, offset + 12);

        int groupOffset = offset + 16;
        if (groupOffset + numGroups * 12 > data.Length) return;

        for (int g = 0; g < numGroups; g++)
        {
            uint startCp = ReadUInt32BE(data, groupOffset + g * 12);
            uint endCp = ReadUInt32BE(data, groupOffset + g * 12 + 4);
            if (endCp < startCp) continue;

            // Clamp to valid Unicode and keep the worst-case bounded.
            if (startCp > 0x10FFFF) continue;
            if (endCp > 0x10FFFF) endCp = 0x10FFFF;

            uint span = endCp - startCp + 1;
            if (span > 0x10FFFF) span = 0x10FFFF;
            for (uint cp = startCp; cp <= endCp; cp++)
            {
                result.Add((int)cp);
            }
        }
    }

    private static Dictionary<int, string> ParseNameTable(byte[] data, int offset, int length)
    {
        var names = new Dictionary<int, string>();

        if (offset + 6 > data.Length) return names;

        var count = ReadUInt16BE(data, offset + 2);
        var stringOffset = ReadUInt16BE(data, offset + 4);
        var storageOffset = offset + stringOffset;

        for (int i = 0; i < count; i++)
        {
            var recordOffset = offset + 6 + (i * 12);
            if (recordOffset + 12 > data.Length) break;

            var platformId = ReadUInt16BE(data, recordOffset);
            var encodingId = ReadUInt16BE(data, recordOffset + 2);
            var languageId = ReadUInt16BE(data, recordOffset + 4);
            var nameId = ReadUInt16BE(data, recordOffset + 6);
            var strLength = ReadUInt16BE(data, recordOffset + 8);
            var strOffset = ReadUInt16BE(data, recordOffset + 10);

            var strStart = storageOffset + strOffset;
            if (strStart + strLength > data.Length) continue;

            string value;

            // Prefer Windows platform (3) with Unicode BMP (1)
            if (platformId == 3 && encodingId == 1)
            {
                value = System.Text.Encoding.BigEndianUnicode.GetString(data, strStart, strLength);
            }
            // Mac Roman (platform 1, encoding 0)
            else if (platformId == 1 && encodingId == 0)
            {
                value = System.Text.Encoding.ASCII.GetString(data, strStart, strLength);
            }
            // Unicode platform (0)
            else if (platformId == 0)
            {
                value = System.Text.Encoding.BigEndianUnicode.GetString(data, strStart, strLength);
            }
            else
            {
                continue;
            }

            // Prefer English (languageId 0x0409 for Windows, 0 for Mac/Unicode)
            if (!names.ContainsKey(nameId) ||
                (platformId == 3 && languageId == 0x0409) ||
                (platformId == 0 && languageId == 0))
            {
                names[nameId] = value.Trim('\0');
            }
        }

        return names;
    }

    private static ushort ReadUInt16BE(byte[] data, int offset)
    {
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private static short ReadInt16BE(byte[] data, int offset)
        => unchecked((short)ReadUInt16BE(data, offset));

    private static uint ReadUInt32BE(byte[] data, int offset)
    {
        return (uint)((data[offset] << 24) | (data[offset + 1] << 16) |
                       (data[offset + 2] << 8) | data[offset + 3]);
    }

    /// <summary>OpenType <c>Fixed</c> (16.16) → double.</summary>
    private static double ReadFixed1616(byte[] data, int offset)
    {
        // High 16 bits: signed integer part. Low 16 bits: unsigned fraction.
        short whole = ReadInt16BE(data, offset);
        ushort frac = ReadUInt16BE(data, offset + 2);
        return whole + frac / 65536.0;
    }

    /// <summary>
    /// OpenType <c>LONGDATETIME</c> (int64, seconds since 1904-01-01 UTC) →
    /// ISO-8601 string. Returns empty string for out-of-range values so the
    /// UI doesn't have to special-case them.
    /// </summary>
    private static string ReadLongDateTime(byte[] data, int offset)
    {
        long secondsSince1904 =
            ((long)data[offset] << 56) |
            ((long)data[offset + 1] << 48) |
            ((long)data[offset + 2] << 40) |
            ((long)data[offset + 3] << 32) |
            ((long)data[offset + 4] << 24) |
            ((long)data[offset + 5] << 16) |
            ((long)data[offset + 6] << 8) |
            ((long)data[offset + 7]);

        try
        {
            var epoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var dt = epoch.AddSeconds(secondsSince1904);
            return dt.ToString("u"); // "yyyy-MM-dd HH:mm:ssZ"
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Decodes head.macStyle bits into a comma-separated label list.</summary>
    private static string DecodeMacStyle(ushort flags)
    {
        var parts = new List<string>(4);
        if ((flags & 0x0001) != 0) parts.Add("Bold");
        if ((flags & 0x0002) != 0) parts.Add("Italic");
        if ((flags & 0x0004) != 0) parts.Add("Underline");
        if ((flags & 0x0008) != 0) parts.Add("Outline");
        if ((flags & 0x0010) != 0) parts.Add("Shadow");
        if ((flags & 0x0020) != 0) parts.Add("Condensed");
        if ((flags & 0x0040) != 0) parts.Add("Extended");
        return parts.Count == 0 ? "Regular" : string.Join(", ", parts);
    }

    /// <summary>
    /// Decodes OS/2.fsType embedding-permission bits into a short human
    /// label following the OpenType spec's interpretation of the lowest
    /// permission bits as mutually exclusive levels.
    /// </summary>
    private static string DecodeFsType(int fsType)
    {
        // Bits 1, 2, 3, 8 are exclusive: pick the most restrictive.
        // Bit 9 (no subsetting) and bit 10 (bitmap-only) are independent
        // flags we report alongside.
        string level;
        if ((fsType & 0x0002) != 0) level = "Restricted";
        else if ((fsType & 0x0004) != 0) level = "Preview & Print";
        else if ((fsType & 0x0008) != 0) level = "Editable";
        else if ((fsType & 0x0200) != 0) level = "Installable (no subset)";
        else level = "Installable";

        var extras = new List<string>(2);
        if ((fsType & 0x0100) != 0) extras.Add("no subsetting");
        if ((fsType & 0x0200) != 0 && level != "Installable (no subset)") extras.Add("bitmap-only embedding");
        return extras.Count == 0 ? level : $"{level} ({string.Join(", ", extras)})";
    }

    private static string ReadTag(byte[] data, int offset)
    {
        return new string(new[]
        {
            (char)data[offset],
            (char)data[offset + 1],
            (char)data[offset + 2],
            (char)data[offset + 3]
        });
    }
}
