namespace Fontager.Core.Helpers;

/// <summary>
/// Curated list of Unicode blocks the glyph grid filters by. Not exhaustive —
/// we include the blocks designers actually use; everything else falls into
/// the synthetic "Other" bucket.
/// </summary>
public static class UnicodeBlocks
{
    public sealed record UnicodeBlock(string Name, int Start, int End)
    {
        public bool Contains(int codePoint) => codePoint >= Start && codePoint <= End;
    }

    public static IReadOnlyList<UnicodeBlock> All { get; } =
    [
        new("Basic Latin", 0x0020, 0x007E),
        new("Latin-1 Supplement", 0x00A0, 0x00FF),
        new("Latin Extended-A", 0x0100, 0x017F),
        new("Latin Extended-B", 0x0180, 0x024F),
        new("IPA Extensions", 0x0250, 0x02AF),
        new("Spacing Modifier Letters", 0x02B0, 0x02FF),
        new("Combining Diacritical Marks", 0x0300, 0x036F),
        new("Greek and Coptic", 0x0370, 0x03FF),
        new("Cyrillic", 0x0400, 0x04FF),
        new("Cyrillic Supplement", 0x0500, 0x052F),
        new("Armenian", 0x0530, 0x058F),
        new("Hebrew", 0x0590, 0x05FF),
        new("Arabic", 0x0600, 0x06FF),
        new("Syriac", 0x0700, 0x074F),
        new("Devanagari", 0x0900, 0x097F),
        new("Bengali", 0x0980, 0x09FF),
        new("Thai", 0x0E00, 0x0E7F),
        new("Latin Extended Additional", 0x1E00, 0x1EFF),
        new("Greek Extended", 0x1F00, 0x1FFF),
        new("General Punctuation", 0x2000, 0x206F),
        new("Superscripts and Subscripts", 0x2070, 0x209F),
        new("Currency Symbols", 0x20A0, 0x20CF),
        new("Letterlike Symbols", 0x2100, 0x214F),
        new("Number Forms", 0x2150, 0x218F),
        new("Arrows", 0x2190, 0x21FF),
        new("Mathematical Operators", 0x2200, 0x22FF),
        new("Miscellaneous Technical", 0x2300, 0x23FF),
        new("Box Drawing", 0x2500, 0x257F),
        new("Block Elements", 0x2580, 0x259F),
        new("Geometric Shapes", 0x25A0, 0x25FF),
        new("Miscellaneous Symbols", 0x2600, 0x26FF),
        new("Dingbats", 0x2700, 0x27BF),
        new("CJK Symbols and Punctuation", 0x3000, 0x303F),
        new("Hiragana", 0x3040, 0x309F),
        new("Katakana", 0x30A0, 0x30FF),
        new("CJK Unified Ideographs", 0x4E00, 0x9FFF),
        new("Hangul Syllables", 0xAC00, 0xD7AF),
        new("Private Use Area", 0xE000, 0xF8FF),
        new("Alphabetic Presentation Forms", 0xFB00, 0xFB4F),
        new("Specials", 0xFFF0, 0xFFFF),
        new("Emoticons", 0x1F600, 0x1F64F),
        new("Miscellaneous Symbols and Pictographs", 0x1F300, 0x1F5FF),
        new("Transport and Map Symbols", 0x1F680, 0x1F6FF),
        new("Supplemental Symbols and Pictographs", 0x1F900, 0x1F9FF),
    ];

    private static readonly UnicodeBlock Other = new("Other", -1, -1);

    /// <summary>
    /// Returns the curated block that contains <paramref name="codePoint"/>, or
    /// a synthetic "Other" block when no curated entry matches.
    /// </summary>
    public static UnicodeBlock GetBlock(int codePoint)
    {
        foreach (var block in All)
        {
            if (block.Contains(codePoint)) return block;
        }
        return Other;
    }
}
