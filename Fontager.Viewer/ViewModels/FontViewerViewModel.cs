using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Fontager.Core.Helpers;
using Fontager.Core.Models;
using Fontager.Core.Services;
using Microsoft.UI.Xaml;

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

    // ── Version Text (for empty-state display) ────────────────

    public string CurrentVersionText
    {
        get
        {
            string version;
            if (FileAssociationService.IsRunningPackaged)
            {
                try
                {
                    var ver = Windows.ApplicationModel.Package.Current.Id.Version;
                    version = $"{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}";
                }
                catch
                {
                    version = AssemblyVersionFallback();
                }
            }
            else
            {
                version = AssemblyVersionFallback();
            }
            return $"Version {version}";
        }
    }

    private static string AssemblyVersionFallback()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return asm != null ? $"{asm.Major}.{asm.Minor}.{asm.Build}" : "0.0.0";
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

    // ── Observable Glyph Filters & State ───────────────────────

    public ObservableCollection<GlyphBlockEntry> GlyphBlockEntries { get; } = [];

    public Visibility GetGlyphDetailVisibility(bool isSelected)
    {
        return isSelected ? Visibility.Visible : Visibility.Collapsed;
    }

    [ObservableProperty]
    private GlyphCategory _selectedCategory = GlyphCategory.All;

    [ObservableProperty]
    private GlyphBlockEntry? _selectedBlockEntry;

    [ObservableProperty]
    private string _glyphSearchText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<GlyphItem> _filteredGlyphs = Array.Empty<GlyphItem>();

    [ObservableProperty]
    private GlyphItem? _selectedGlyph;

    public bool IsGlyphSelected => SelectedGlyph is not null;

    partial void OnSelectedGlyphChanged(GlyphItem? value)
    {
        OnPropertyChanged(nameof(IsGlyphSelected));
    }

    private CancellationTokenSource? _searchCts;
    private bool _suppressFiltering;

    partial void OnSelectedCategoryChanged(GlyphCategory value) => ApplyFilters();

    partial void OnSelectedBlockEntryChanged(GlyphBlockEntry? value) => ApplyFilters();

    partial void OnGlyphSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        Task.Delay(150, token).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && !token.IsCancellationRequested)
            {
                ApplyFilters();
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    public void BuildBlockSidebar()
    {
        GlyphBlockEntries.Clear();

        var perBlockCounts = new Dictionary<string, (UnicodeBlocks.UnicodeBlock? Block, int Count)>();
        foreach (var item in GlyphItems)
        {
            var block = item.Block;
            var key = block.Name;
            if (perBlockCounts.TryGetValue(key, out var existing))
            {
                perBlockCounts[key] = (existing.Block, existing.Count + 1);
            }
            else
            {
                perBlockCounts[key] = (block.Start >= 0 ? block : null, 1);
            }
        }

        GlyphBlockEntries.Add(new GlyphBlockEntry("All blocks", GlyphItems.Count, null));

        // Preserve the curated order from UnicodeBlocks.All; "Other" goes last.
        foreach (var b in UnicodeBlocks.All)
        {
            if (perBlockCounts.TryGetValue(b.Name, out var entry))
                GlyphBlockEntries.Add(new GlyphBlockEntry(b.Name, entry.Count, b));
        }
        if (perBlockCounts.TryGetValue("Other", out var other))
            GlyphBlockEntries.Add(new GlyphBlockEntry("Other", other.Count, null));
    }

    public void SelectPreferredBlock()
    {
        if (GlyphBlockEntries.Count == 0) return;
        var basic = GlyphBlockEntries.FirstOrDefault(e => e.Name == "Basic Latin" && e.Count > 0);
        SelectedBlockEntry = basic ?? GlyphBlockEntries[0];
    }

    public void ApplyFilters()
    {
        if (_suppressFiltering) return;

        var blockEntry = SelectedBlockEntry;
        var blockFilter = blockEntry?.Block;
        var otherOnly = blockEntry?.Name == "Other";
        var category = SelectedCategory;
        var needle = GlyphSearchText.Trim();
        var matcher = string.IsNullOrEmpty(needle) ? null : BuildSearchMatcher(needle);

        var filtered = new List<GlyphItem>(capacity: GlyphItems.Count);
        foreach (var g in GlyphItems)
        {
            if (blockFilter is not null && !blockFilter.Contains(g.CodePoint)) continue;
            if (otherOnly && g.Block.Start >= 0) continue;
            if (category != GlyphCategory.All && g.Category != category) continue;
            if (matcher is not null && !matcher(g)) continue;
            filtered.Add(g);
        }

        FilteredGlyphs = filtered;
    }

    private static Func<GlyphItem, bool> BuildSearchMatcher(string needle)
    {
        // Hex form: "U+xxxx" or "0xXXXX"
        if (needle.StartsWith("U+", StringComparison.OrdinalIgnoreCase)
            || needle.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var hex = needle[2..];
            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var cpHex))
                return g => g.CodePoint == cpHex;
        }

        // Bare hex (4+ digits, all hex)
        if (needle.Length >= 4 && needle.All(c => Uri.IsHexDigit(c)))
        {
            if (int.TryParse(needle, System.Globalization.NumberStyles.HexNumber, null, out var cpHex))
                return g => g.CodePoint == cpHex;
        }

        // Decimal
        if (int.TryParse(needle, out var cpDec))
            return g => g.CodePoint == cpDec;

        // Single character literal
        if (needle.Length <= 2)
        {
            try
            {
                var cp = char.ConvertToUtf32(needle, 0);
                return g => g.CodePoint == cp;
            }
            catch
            {
                // fall through
            }
        }

        // Substring on hex label (covers U+1F600 etc)
        return g => g.UnicodeLabel.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

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

            // Build block sidebar counts
            BuildBlockSidebar();

            // Reset active filters
            _suppressFiltering = true;
            try
            {
                SelectedCategory = GlyphCategory.All;
                GlyphSearchText = string.Empty;
                SelectPreferredBlock();
            }
            finally
            {
                _suppressFiltering = false;
            }

            ApplyFilters();
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
    public string DetailsLabel => $"Decimal: {CodePoint} \u00b7 Block: {Block.Name} \u00b7 Category: {Category}";

    public GlyphItem(int codePoint)
    {
        CodePoint = codePoint;
        Character = char.ConvertFromUtf32(codePoint);
        UnicodeLabel = $"U+{codePoint:X4}";
        Block = UnicodeBlocks.GetBlock(codePoint);
        Category = GlyphCategoryClassifier.Classify(codePoint);
    }
}

public sealed record GlyphBlockEntry(string Name, int Count, UnicodeBlocks.UnicodeBlock? Block);
