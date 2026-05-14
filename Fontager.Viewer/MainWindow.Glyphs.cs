using System;
using System.Collections.Generic;
using System.Linq;
using Fontager.Core.Helpers;
using Fontager.Core.Models;
using Fontager.Viewer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Fontager.Viewer;

/// <summary>
/// Glyph-tab logic for <see cref="MainWindow"/>. Split out of
/// <c>MainWindow.xaml.cs</c> because the tab is its own little app with
/// three composable filters (Unicode block sidebar, functional category
/// chips, free-text search) plus a debounced search input.
///
/// <para>The pieces, in the order they fire:</para>
/// <list type="number">
///   <item><description><see cref="BuildGlyphGrid"/> is the entry point —
///     called once per font load. It generates the master glyph list from
///     the font's cmap, rebuilds the sidebar + chips against that list,
///     resets the active filters to "everything", and finally rewires the
///     GridView's <c>ContainerContentChanging</c> so each realized cell
///     gets the loaded face (FontFamily set on the GridView itself does
///     not reliably inherit through item templates).</description></item>
///   <item><description><see cref="ApplyGlyphFilters"/> is the hot path —
///     every block-pick / chip-toggle / search-keystroke funnels here. It
///     builds one composite predicate and walks the master list once to
///     avoid LINQ allocation overhead with 10k+ glyphs.</description></item>
///   <item><description><see cref="BuildSearchMatcher"/> is the search
///     parser — it accepts <c>U+00A0</c>, <c>0x00A0</c>, bare hex, decimal
///     code points, or literal characters; falls back to a substring match
///     on the hex label.</description></item>
/// </list>
/// </summary>
public sealed partial class MainWindow
{
    // ── Glyph Grid ─────────────────────────────────────────────

    /// <summary>
    /// Rebuilds everything related to the Glyphs tab when a new font loads:
    /// the master glyph list, the Unicode-block sidebar, the functional
    /// category chips, and resets the active filters.
    /// </summary>
    private void BuildGlyphGrid()
    {
        _viewModel.GenerateGlyphItemsPublic();

        BuildBlockSidebar();
        BuildCategoryChips();
        ResetGlyphFilters();
        ApplyGlyphFilters();

        // GridView item roots live under GridViewItem, not as logical children of
        // the GridView itself — FontFamily set on the GridView does NOT reliably
        // inherit into DataTemplate TextBlocks (they keep the system UI font).
        // Apply the loaded face per realized container only; virtualization means
        // we only touch on-screen rows.
        GlyphGrid.ContainerContentChanging -= GlyphGrid_ContainerContentChanging;
        GlyphGrid.ContainerContentChanging += GlyphGrid_ContainerContentChanging;

        if (_loadedFontFamily is not null)
            GlyphGrid.FontFamily = _loadedFontFamily;
    }

    private void GlyphGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Phase != 0 || _loadedFontFamily is null)
            return;

        args.RegisterUpdateCallback(GlyphGrid_ApplyFontToItemContainer);
    }

    private void GlyphGrid_ApplyFontToItemContainer(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (_loadedFontFamily is null)
            return;

        if (args.ItemContainer?.ContentTemplateRoot is StackPanel panel
            && panel.Children.Count > 0
            && panel.Children[0] is TextBlock charBlock)
        {
            charBlock.FontFamily = _loadedFontFamily;
            charBlock.FontWeight = new Windows.UI.Text.FontWeight(400);
            charBlock.FontStyle = Windows.UI.Text.FontStyle.Normal;
        }
    }

    private void BuildBlockSidebar()
    {
        _glyphBlockEntries.Clear();

        var perBlockCounts = new Dictionary<string, (UnicodeBlocks.UnicodeBlock? Block, int Count)>();
        foreach (var item in _viewModel.GlyphItems)
        {
            // item.Block is precomputed in the GlyphItem constructor — no
            // per-pass classifier work here.
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

        _glyphBlockEntries.Add(new GlyphBlockEntry("All blocks", _viewModel.GlyphItems.Count, null));

        // Preserve the curated order from UnicodeBlocks.All; "Other" goes last.
        foreach (var b in UnicodeBlocks.All)
        {
            if (perBlockCounts.TryGetValue(b.Name, out var entry))
                _glyphBlockEntries.Add(new GlyphBlockEntry(b.Name, entry.Count, b));
        }
        if (perBlockCounts.TryGetValue("Other", out var other))
            _glyphBlockEntries.Add(new GlyphBlockEntry("Other", other.Count, null));

        GlyphBlockList.ItemsSource = _glyphBlockEntries;
    }

    private void BuildCategoryChips()
    {
        GlyphCategoryChips.Children.Clear();

        foreach (GlyphCategory cat in Enum.GetValues<GlyphCategory>())
        {
            var btn = new ToggleButton
            {
                Content = cat.ToString(),
                Tag = cat,
                MinWidth = 0,
                Padding = new Thickness(10, 4, 10, 4),
                IsChecked = cat == GlyphCategory.All
            };
            btn.Click += GlyphCategoryChip_Click;
            GlyphCategoryChips.Children.Add(btn);
        }
    }

    private void ResetGlyphFilters()
    {
        _suppressGlyphFilterEvents = true;
        try
        {
            _glyphCategoryFilter = GlyphCategory.All;
            _glyphBlockFilter = null;
            _glyphSearchText = string.Empty;

            foreach (var child in GlyphCategoryChips.Children)
            {
                if (child is ToggleButton tb && tb.Tag is GlyphCategory cat)
                    tb.IsChecked = cat == GlyphCategory.All;
            }

            if (_glyphBlockEntries.Count > 0)
                GlyphBlockList.SelectedIndex = 0;

            GlyphSearchBox.Text = string.Empty;
        }
        finally
        {
            _suppressGlyphFilterEvents = false;
        }
    }

    private void ApplyGlyphFilters()
    {
        // Build a single composite predicate and run it once over the master
        // list. Two wins over chained LINQ Wheres:
        //   1. one allocation for the result list instead of N enumerators,
        //   2. JIT can keep the precomputed-property reads tight.
        var blockFilter = _glyphBlockFilter;
        var otherOnly = blockFilter is null
            && GlyphBlockList.SelectedItem is GlyphBlockEntry entry
            && entry.Name == "Other";
        var category = _glyphCategoryFilter;
        var needle = _glyphSearchText.Trim();
        var matcher = string.IsNullOrEmpty(needle) ? null : BuildSearchMatcher(needle);

        var master = _viewModel.GlyphItems;
        var filtered = new List<GlyphItem>(capacity: master.Count);
        foreach (var g in master)
        {
            if (blockFilter is not null && !blockFilter.Contains(g.CodePoint)) continue;
            if (otherOnly && g.Block.Start >= 0) continue;
            if (category != GlyphCategory.All && g.Category != category) continue;
            if (matcher is not null && !matcher(g)) continue;
            filtered.Add(g);
        }

        GlyphGrid.ItemsSource = filtered;
        GlyphCountText.Text = filtered.Count.ToString();
        GlyphDetailPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Builds a predicate that matches a glyph by raw character ("A"), hex code
    /// point ("U+00A0" / "00A0" / "0x00A0"), or decimal code point ("9731").
    /// </summary>
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

        // Substring on hex label (covers things like "F60" hitting U+1F600..U+1F60F)
        return g => g.UnicodeLabel.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private void GlyphGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GlyphGrid.SelectedItem is GlyphItem glyph)
        {
            GlyphDetailPanel.Visibility = Visibility.Visible;
            SelectedGlyphChar.Text = glyph.Character;
            SelectedGlyphUnicode.Text = glyph.UnicodeLabel;
            SelectedGlyphName.Text =
                $"Decimal: {glyph.CodePoint} \u00b7 Block: {glyph.Block.Name} \u00b7 Category: {glyph.Category}";

            if (_loadedFontFamily != null)
            {
                SelectedGlyphChar.FontFamily = _loadedFontFamily;
                SelectedGlyphChar.FontWeight = new Windows.UI.Text.FontWeight(400);
                SelectedGlyphChar.FontStyle = Windows.UI.Text.FontStyle.Normal;
            }
        }
        else
        {
            GlyphDetailPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void GlyphBlockList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressGlyphFilterEvents) return;
        if (GlyphBlockList.SelectedItem is GlyphBlockEntry entry)
        {
            _glyphBlockFilter = entry.Block;
            ApplyGlyphFilters();
        }
    }

    private void GlyphCategoryChip_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressGlyphFilterEvents) return;
        if (sender is not ToggleButton btn || btn.Tag is not GlyphCategory selected) return;

        _suppressGlyphFilterEvents = true;
        try
        {
            // Single-select: clear any previous chip, force the clicked one on.
            foreach (var child in GlyphCategoryChips.Children)
            {
                if (child is ToggleButton other && other.Tag is GlyphCategory cat)
                    other.IsChecked = cat == selected;
            }
            _glyphCategoryFilter = selected;
        }
        finally
        {
            _suppressGlyphFilterEvents = false;
        }
        ApplyGlyphFilters();
    }

    private void GlyphSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_suppressGlyphFilterEvents) return;
        _glyphSearchText = sender.Text ?? string.Empty;

        // Debounce: cancel any pending filter and re-arm. The reason for going
        // through DispatcherQueueTimer rather than a Task.Delay+cancellation
        // pattern is that the timer Tick already lands on the UI thread, so
        // touching GlyphGrid / GlyphCountText is safe without marshalling.
        if (_glyphSearchDebounceTimer is null)
        {
            _glyphSearchDebounceTimer = DispatcherQueue.CreateTimer();
            _glyphSearchDebounceTimer.IsRepeating = false;
            _glyphSearchDebounceTimer.Tick += (_, _) => ApplyGlyphFilters();
        }

        _glyphSearchDebounceTimer.Interval = TimeSpan.FromMilliseconds(GlyphSearchDebounceMs);
        _glyphSearchDebounceTimer.Stop();
        _glyphSearchDebounceTimer.Start();
    }

    /// <summary>Sidebar row model.</summary>
    private sealed record GlyphBlockEntry(string Name, int Count, UnicodeBlocks.UnicodeBlock? Block);
}
