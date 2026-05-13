using System.Globalization;
using System.Text;

namespace Fontager.Core.Models;

/// <summary>
/// Functional buckets used by the glyph filter chips, derived from a code
/// point's <see cref="UnicodeCategory"/>. These are deliberately broad — the
/// glyph grid uses both this and the Unicode-block sidebar as orthogonal axes.
/// </summary>
public enum GlyphCategory
{
    All,
    Uppercase,
    Lowercase,
    Numbers,
    Punctuation,
    Symbols,
    Accented,
    Other
}

public static class GlyphCategoryClassifier
{
    /// <summary>
    /// Categorizes a single Unicode code point. Returns <see cref="GlyphCategory.Other"/>
    /// for code points that don't cleanly map to any of the well-known buckets
    /// (control chars, format chars, separators, etc.).
    /// </summary>
    public static GlyphCategory Classify(int codePoint)
    {
        var category = CharUnicodeInfo.GetUnicodeCategory(codePoint);

        switch (category)
        {
            case UnicodeCategory.UppercaseLetter:
                return IsAccented(codePoint) ? GlyphCategory.Accented : GlyphCategory.Uppercase;

            case UnicodeCategory.LowercaseLetter:
                return IsAccented(codePoint) ? GlyphCategory.Accented : GlyphCategory.Lowercase;

            case UnicodeCategory.TitlecaseLetter:
            case UnicodeCategory.ModifierLetter:
            case UnicodeCategory.OtherLetter:
                return IsAccented(codePoint) ? GlyphCategory.Accented : GlyphCategory.Other;

            case UnicodeCategory.DecimalDigitNumber:
            case UnicodeCategory.LetterNumber:
            case UnicodeCategory.OtherNumber:
                return GlyphCategory.Numbers;

            case UnicodeCategory.ConnectorPunctuation:
            case UnicodeCategory.DashPunctuation:
            case UnicodeCategory.OpenPunctuation:
            case UnicodeCategory.ClosePunctuation:
            case UnicodeCategory.InitialQuotePunctuation:
            case UnicodeCategory.FinalQuotePunctuation:
            case UnicodeCategory.OtherPunctuation:
                return GlyphCategory.Punctuation;

            case UnicodeCategory.MathSymbol:
            case UnicodeCategory.CurrencySymbol:
            case UnicodeCategory.ModifierSymbol:
            case UnicodeCategory.OtherSymbol:
                return GlyphCategory.Symbols;

            default:
                return GlyphCategory.Other;
        }
    }

    /// <summary>
    /// A code point is "Accented" if NFD decomposition produces more than one
    /// scalar, i.e. it composes a base letter with one or more combining marks.
    /// Cheap and works without a hand-rolled lookup table.
    /// </summary>
    private static bool IsAccented(int codePoint)
    {
        try
        {
            var s = char.ConvertFromUtf32(codePoint);
            var decomposed = s.Normalize(NormalizationForm.FormD);
            // A multi-rune NFD means at least one combining mark attached.
            if (decomposed.Length <= s.Length) return false;

            // Make sure the extra scalars include a combining mark.
            for (int i = 0; i < decomposed.Length;)
            {
                int cp = char.ConvertToUtf32(decomposed, i);
                var cat = CharUnicodeInfo.GetUnicodeCategory(cp);
                if (cat == UnicodeCategory.NonSpacingMark
                    || cat == UnicodeCategory.SpacingCombiningMark
                    || cat == UnicodeCategory.EnclosingMark)
                {
                    return true;
                }
                i += char.IsHighSurrogate(decomposed[i]) ? 2 : 1;
            }
        }
        catch
        {
            // Out-of-range code points fall through.
        }
        return false;
    }
}
