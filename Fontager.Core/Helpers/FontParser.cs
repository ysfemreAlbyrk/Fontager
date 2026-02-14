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
    /// Parses a font file and extracts its metadata.
    /// </summary>
    public static FontMetadata Parse(string filePath)
    {
        try
        {
            var data = File.ReadAllBytes(filePath);
            return Parse(data);
        }
        catch
        {
            return new FontMetadata();
        }
    }

    /// <summary>
    /// Parses font binary data and extracts metadata.
    /// </summary>
    public static FontMetadata Parse(byte[] data)
    {
        if (data.Length < 12)
            return new FontMetadata();

        try
        {
            var sfVersion = ReadUInt32BE(data, 0);

            // TTC header - parse first font in collection
            int tableOffset = 0;
            if (sfVersion == 0x74746366) // 'ttcf'
            {
                if (data.Length < 16)
                    return new FontMetadata();
                // Read offset to first font
                tableOffset = (int)ReadUInt32BE(data, 12);
            }

            var numTables = ReadUInt16BE(data, tableOffset + 4);

            int nameTableOffset = -1;
            int nameTableLength = 0;
            int os2TableOffset = -1;
            int os2TableLength = 0;
            int headTableOffset = -1;
            int maxpTableOffset = -1;
            int fvarTableOffset = -1;

            // Read table directory
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

            return new FontMetadata
            {
                FamilyName = names.GetValueOrDefault(NameId_FontFamily, string.Empty),
                SubfamilyName = names.GetValueOrDefault(NameId_FontSubfamily, string.Empty),
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
                Classification = classification
            };
        }
        catch
        {
            return new FontMetadata();
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
