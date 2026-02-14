using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
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

    public ObservableCollection<GlyphItem> GlyphItems { get; } = [];

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
    /// Generates glyph items for the character map. Public for code-behind access.
    /// </summary>
    public void GenerateGlyphItemsPublic()
    {
        GlyphItems.Clear();

        // Basic Latin (U+0020 - U+007E)
        for (int cp = 0x0020; cp <= 0x007E; cp++)
        {
            GlyphItems.Add(new GlyphItem(cp));
        }

        // Latin-1 Supplement (U+00A0 - U+00FF)
        for (int cp = 0x00A0; cp <= 0x00FF; cp++)
        {
            GlyphItems.Add(new GlyphItem(cp));
        }

        // Latin Extended-A (U+0100 - U+017F)
        for (int cp = 0x0100; cp <= 0x017F; cp++)
        {
            GlyphItems.Add(new GlyphItem(cp));
        }
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
/// </summary>
public record GlyphItem(int CodePoint)
{
    public string Character => char.ConvertFromUtf32(CodePoint);
    public string UnicodeLabel => $"U+{CodePoint:X4}";
}
