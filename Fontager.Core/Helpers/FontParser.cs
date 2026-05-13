using Fontager.Core.Models;

namespace Fontager.Core.Helpers;

/// <summary>
/// Parses TTF/OTF font files to extract metadata from name and OS/2 tables.
/// Reads raw binary data without any external dependencies.
/// </summary>
public static class FontParser
{
    // Name table Name IDs
    private const int NameId_Copyright = 0;
    private const int NameId_FontFamily = 1;
    private const int NameId_FontSubfamily = 2;
    private const int NameId_FullFontName = 4;
    private const int NameId_Version = 5;
    private const int NameId_PostScriptName = 6;
    private const int NameId_Trademark = 7;
    private const int NameId_Manufacturer = 8;
    private const int NameId_Designer = 9;
    private const int NameId_Description = 10;
    private const int NameId_LicenseDescription = 13;
    private const int NameId_LicenseUrl = 14;
    private const int NameId_TypographicFamily = 16;
    private const int NameId_TypographicSubfamily = 17;

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
            int maxpTableOffset = -1;
            int fvarTableOffset = -1;
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
                    case "maxp":
                        maxpTableOffset = offset;
                        break;
                    case "fvar":
                        fvarTableOffset = offset;
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

            // Parse OS/2 table
            int weight = 400;
            bool isItalic = false;
            bool isOblique = false;
            FontClassification classification = FontClassification.None;
            string vendor = string.Empty;

            if (os2TableOffset >= 0 && os2TableOffset + 10 <= data.Length)
            {
                weight = ReadUInt16BE(data, os2TableOffset + 4); // usWeightClass
                if (weight < 100) weight = 100;
                if (weight > 900) weight = 900;

                if (os2TableOffset + 62 <= data.Length)
                {
                    var fsSelection = ReadUInt16BE(data, os2TableOffset + 62);
                    isItalic = (fsSelection & 0x01) != 0;
                    isOblique = (fsSelection & 0x200) != 0;
                }

                // Panose classification (offset 32-41)
                if (os2TableOffset + 36 <= data.Length)
                {
                    var panoseFamily = data[os2TableOffset + 32];
                    classification = panoseFamily switch
                    {
                        2 => FontClassification.Serif,     // Latin Text
                        3 => FontClassification.Script,    // Latin Hand Written
                        4 => FontClassification.Display,   // Latin Decoratives
                        5 => FontClassification.Symbol,    // Latin Symbol
                        _ => FontClassification.None
                    };
                }

                // achVendID (offset 58-61)
                if (os2TableOffset + 62 <= data.Length)
                {
                    vendor = ReadTag(data, os2TableOffset + 58);
                }
            }

            // Parse head table for unitsPerEm
            int unitsPerEm = 0;
            if (headTableOffset >= 0 && headTableOffset + 20 <= data.Length)
            {
                unitsPerEm = ReadUInt16BE(data, headTableOffset + 18);
            }

            // Parse maxp table for glyph count
            int glyphCount = 0;
            if (maxpTableOffset >= 0 && maxpTableOffset + 6 <= data.Length)
            {
                glyphCount = ReadUInt16BE(data, maxpTableOffset + 4);
            }

            // Check for variable font (fvar table exists)
            bool isVariable = fvarTableOffset >= 0;

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

            return new FontMetadata
            {
                FamilyName = basicFamily,
                TypographicFamilyName = !string.IsNullOrWhiteSpace(typoFamily) ? typoFamily : basicFamily,
                SubfamilyName = names.GetValueOrDefault(NameId_TypographicSubfamily,
                    names.GetValueOrDefault(NameId_FontSubfamily, string.Empty)),
                FullName = names.GetValueOrDefault(NameId_FullFontName, string.Empty),
                PostScriptName = names.GetValueOrDefault(NameId_PostScriptName, string.Empty),
                Designer = names.GetValueOrDefault(NameId_Designer, string.Empty),
                Description = names.GetValueOrDefault(NameId_Description, string.Empty),
                License = names.GetValueOrDefault(NameId_LicenseDescription, string.Empty),
                LicenseUrl = names.GetValueOrDefault(NameId_LicenseUrl, string.Empty),
                Vendor = vendor.Trim('\0', ' '),
                Version = names.GetValueOrDefault(NameId_Version, string.Empty),
                Copyright = names.GetValueOrDefault(NameId_Copyright, string.Empty),
                Trademark = names.GetValueOrDefault(NameId_Trademark, string.Empty),
                GlyphCount = glyphCount,
                IsVariable = isVariable,
                UnitsPerEm = unitsPerEm,
                Weight = weight,
                IsItalic = isItalic,
                IsOblique = isOblique,
                Classification = classification,
                SupportedCodePoints = supportedCodePoints
            };
        }
        catch
        {
            return new FontMetadata();
        }
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

    private static uint ReadUInt32BE(byte[] data, int offset)
    {
        return (uint)((data[offset] << 24) | (data[offset + 1] << 16) |
                       (data[offset + 2] << 8) | data[offset + 3]);
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
