using System;
using System.ComponentModel;
using Fontager.Core.Models;
using Fontager.Viewer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Text;

namespace Fontager.Viewer.Views;

public sealed partial class GlyphsTabPage : Page
{
    public FontViewerViewModel ViewModel { get; }

    public GlyphsTabPage()
    {
        InitializeComponent();

        ViewModel = App.Services.GetRequiredService<FontViewerViewModel>();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        BuildGlyphGrid();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // LoadedFontFamily changes AFTER CurrentFont + glyph generation is complete —
        // use it as the signal that everything is ready to refresh the glyph grid.
        if (e.PropertyName == nameof(FontViewerViewModel.LoadedFontFamily))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                BuildGlyphGrid();
            });
        }
    }

    private void BuildGlyphGrid()
    {
        // Set FontFamily on the GridView — used as fallback and for the container callback.
        if (ViewModel.LoadedFontFamily is not null)
            GlyphGrid.FontFamily = ViewModel.LoadedFontFamily;

        // Recompute the filtered glyph list.
        ViewModel.ApplyFilters();

        // Explicit set in case x:Bind was suspended while page was off-screen
        // (NavigationCacheMode="Required" pauses bindings when page leaves visual tree).
        GlyphGrid.ItemsSource = ViewModel.FilteredGlyphs;
        GlyphBlockList.SelectedItem = ViewModel.SelectedBlockEntry;

        // ContainerContentChanging applies FontFamily explicitly to each realized
        // TextBlock — GridViewItem's own style blocks inheritance from the parent
        // GridView, so we must set it per-container.
        GlyphGrid.ContainerContentChanging -= GlyphGrid_ContainerContentChanging;
        GlyphGrid.ContainerContentChanging += GlyphGrid_ContainerContentChanging;

        GlyphGrid.SelectionChanged -= GlyphGrid_SelectionChanged;
        GlyphGrid.SelectionChanged += GlyphGrid_SelectionChanged;

        BuildCategoryChips();
        ResetGlyphFilters();

        ApplySelectedGlyphFontFamily();
    }

    private void GlyphGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Phase != 0) return;
        // Always register Phase 1 — BuildGlyphGrid is only called after
        // LoadedFontFamily is set, so Phase 1 will always have a valid font.
        args.RegisterUpdateCallback(GlyphGrid_ApplyFontToContainer);
    }

    private void GlyphGrid_ApplyFontToContainer(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (ViewModel.LoadedFontFamily is null) return;

        if (args.ItemContainer?.ContentTemplateRoot is Grid grid
            && grid.Children.Count > 0
            && grid.Children[0] is StackPanel panel
            && panel.Children.Count > 0
            && panel.Children[0] is TextBlock charBlock)
        {
            charBlock.FontFamily = ViewModel.LoadedFontFamily;
        }
    }

    private void GlyphGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplySelectedGlyphFontFamily();
    }

    private void ApplySelectedGlyphFontFamily()
    {
        if (ViewModel.LoadedFontFamily is null)
            return;

        SelectedGlyphChar.FontFamily = ViewModel.LoadedFontFamily;
        var meta = ViewModel.CurrentFont?.Metadata;
        if (meta is not null)
        {
            SelectedGlyphChar.FontWeight = new FontWeight((ushort)meta.Weight);
            SelectedGlyphChar.FontStyle = meta.IsItalic ? FontStyle.Italic
                : meta.IsOblique ? FontStyle.Oblique
                : FontStyle.Normal;
        }
        else
        {
            SelectedGlyphChar.FontWeight = new FontWeight(400);
            SelectedGlyphChar.FontStyle = FontStyle.Normal;
        }
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
        foreach (var child in GlyphCategoryChips.Children)
        {
            if (child is ToggleButton tb && tb.Tag is GlyphCategory cat)
                tb.IsChecked = cat == GlyphCategory.All;
        }
    }

    private void GlyphCategoryChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton btn || btn.Tag is not GlyphCategory selected) return;

        foreach (var child in GlyphCategoryChips.Children)
        {
            if (child is ToggleButton other && other.Tag is GlyphCategory cat)
                other.IsChecked = cat == selected;
        }

        ViewModel.SelectedCategory = selected;
    }

    private void GlyphDetailCopy_Click(object sender, RoutedEventArgs e)
    {
        if (GlyphGrid.SelectedItem is not GlyphItem glyph)
            return;

        try
        {
            var package = new DataPackage();
            package.SetText(glyph.Character);
            Clipboard.SetContent(package);
        }
        catch
        {
            return;
        }

        GlyphDetailCopiedNotice.Visibility = Visibility.Visible;

        var timer = DispatcherQueue.CreateTimer();
        timer.IsRepeating = false;
        timer.Interval = TimeSpan.FromMilliseconds(1400);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            GlyphDetailCopiedNotice.Visibility = Visibility.Collapsed;
        };
        timer.Start();
    }

    private void GlyphDetailClose_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedGlyph = null;
    }
}
