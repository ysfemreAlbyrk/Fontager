using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Fontager.Core.Helpers;
using Fontager.Core.Models;
using Fontager.Core.Services;

namespace Fontager.Viewer.ViewModels;

/// <summary>
/// ViewModel for the font viewer, managing preview state and font metadata display.
/// </summary>
public partial class FontViewerViewModel : ObservableObject
{
    private readonly IFontService _fontService;

    public FontViewerViewModel(IFontService fontService)
    {
        _fontService = fontService;
    }

    // ── Font Data ──────────────────────────────────────────────

    [ObservableProperty]
    private FontModel? _currentFont;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasFont;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    // ── Preview Settings ───────────────────────────────────────

    [ObservableProperty]
    private string _previewText = "The quick brown fox jumps over the lazy dog. 0123456789";

    [ObservableProperty]
    private double _previewFontSize = 32;

    // ── Active Tab ─────────────────────────────────────────────

    [ObservableProperty]
    private int _selectedTabIndex;

    // ── Waterfall Sizes ────────────────────────────────────────

    public ObservableCollection<WaterfallItem> WaterfallItems { get; } = [];

    // ── Glyph Grid ─────────────────────────────────────────────

    /// <summary>
    /// Master glyph list. Plain <see cref="List{T}"/> on purpose — this is
    /// never bound to a control (the GridView is bound to a filtered view in
    /// <c>MainWindow.xaml.cs</c>), so the change-notification machinery of
    /// <see cref="ObservableCollection{T}"/> would just be paid-for overhead
    /// every time we rebuild it for a new font (can be 10k+ items).
    /// </summary>
    public List<GlyphItem> GlyphItems { get; } = [];

    // ── Public Methods ─────────────────────────────────────────

    /// <summary>
    /// Loads a font from the given file path at the specified index.
    /// </summary>
    public async Task LoadFontAsync(string filePath, int fontIndex = 0)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            var font = await _fontService.LoadFontAsync(filePath, fontIndex);

            if (font is null)
            {
                HasError = true;
                ErrorMessage = "Could not load font file. The file may be corrupted or unsupported.";
                HasFont = false;
                return;
            }

            CurrentFont = font;
            HasFont = true;

            // Generate waterfall items
            GenerateWaterfallItems();

            // Generate glyph items (basic Latin + common ranges)
            GenerateGlyphItemsPublic();
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Error loading font: {ex.Message}";
            HasFont = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void GenerateWaterfallItems()
    {
        WaterfallItems.Clear();

        int[] sizes = [8, 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 40, 48, 56, 64, 72];
        var text = string.IsNullOrWhiteSpace(PreviewText)
            ? "The quick brown fox jumps over the lazy dog"
            : PreviewText;

        foreach (var size in sizes)
        {
            WaterfallItems.Add(new WaterfallItem(size, text));
        }
    }

    /// <summary>
    /// Generates glyph items for the character map from the font's actual cmap.
    /// Falls back to Basic Latin / Latin-1 / Latin Extended-A when the cmap
    /// could not be parsed (e.g. WOFF2 with the current binary parser).
    /// </summary>
    public void GenerateGlyphItemsPublic()
    {
        GlyphItems.Clear();

        var supported = CurrentFont?.Metadata.SupportedCodePoints;
        if (supported is { Count: > 0 })
        {
            // Reserve capacity up-front to avoid repeated List resizes for
            // large CJK / emoji fonts (can hit 20k+ glyphs).
            GlyphItems.Capacity = Math.Max(GlyphItems.Capacity, supported.Count);

            // Pre-sort once; downstream filtering relies on List order being
            // stable in code-point order.
            var sorted = new int[supported.Count];
            int i = 0;
            foreach (var cp in supported) sorted[i++] = cp;
            Array.Sort(sorted);

            foreach (var cp in sorted)
            {
                if (!IsRenderableCodePoint(cp)) continue;
                GlyphItems.Add(new GlyphItem(cp));
            }
            return;
        }

        // Fallback for fonts whose cmap we could not decode.
        AddRange(0x0020, 0x007E);
        AddRange(0x00A0, 0x00FF);
        AddRange(0x0100, 0x017F);

        void AddRange(int start, int end)
        {
            for (int cp = start; cp <= end; cp++) GlyphItems.Add(new GlyphItem(cp));
        }
    }

    /// <summary>
    /// Skips code points that have no visible glyph (controls, surrogates,
    /// private use, formatting). The cmap can technically claim these.
    /// </summary>
    private static bool IsRenderableCodePoint(int cp)
    {
        if (cp < 0x20) return false;                  // C0 controls
        if (cp == 0x7F) return false;                 // DEL
        if (cp >= 0x80 && cp <= 0x9F) return false;   // C1 controls
        if (cp >= 0xD800 && cp <= 0xDFFF) return false; // surrogate halves
        if (cp > 0x10FFFF) return false;              // out of range
        return true;
    }
}

/// <summary>
/// Represents a single line in the waterfall view.
/// </summary>
public record WaterfallItem(int Size, string Text)
{
    public string SizeLabel => $"{Size}px";
}

/// <summary>
/// Represents a single glyph in the character map grid.
///
/// Designed to be allocated once and read many times — for a CJK font we can
/// have 20k+ of these. Everything (Character / UnicodeLabel / Block /
/// Category) is precomputed in the constructor so per-frame filtering and
/// data-binding stays O(items) with a tiny constant, not O(items × classify).
/// </summary>
public sealed class GlyphItem
{
    public int CodePoint { get; }
    public string Character { get; }
    public string UnicodeLabel { get; }
    public UnicodeBlocks.UnicodeBlock Block { get; }
    public GlyphCategory Category { get; }

    public GlyphItem(int codePoint)
    {
        CodePoint = codePoint;
        Character = char.ConvertFromUtf32(codePoint);
        UnicodeLabel = $"U+{codePoint:X4}";
        Block = UnicodeBlocks.GetBlock(codePoint);
        Category = GlyphCategoryClassifier.Classify(codePoint);
    }
}
