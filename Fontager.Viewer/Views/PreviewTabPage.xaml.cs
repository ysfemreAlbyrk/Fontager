using System;
using System.ComponentModel;
using Fontager.Core.Models;
using Fontager.Viewer.Helpers;
using Fontager.Viewer.Services;
using Fontager.Viewer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace Fontager.Viewer.Views;

public sealed partial class PreviewTabPage : Page
{
    private readonly SettingsService _settings;
    public FontViewerViewModel ViewModel { get; }

    public PreviewTabPage()
    {
        InitializeComponent();

        ViewModel = App.Services.GetRequiredService<FontViewerViewModel>();
        _settings = App.Services.GetRequiredService<SettingsService>();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _settings.Changed += OnSettingsChanged;

        UpdateFontDisplay();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _settings.Changed -= OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateFontDisplay();
        });
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FontViewerViewModel.CurrentFont) ||
            e.PropertyName == nameof(FontViewerViewModel.PreviewText) ||
            e.PropertyName == nameof(FontViewerViewModel.PreviewFontSize) ||
            e.PropertyName == nameof(FontViewerViewModel.LoadedFontFamily))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateFontDisplay();
            });
        }
    }

    private void UpdateFontDisplay()
    {
        var font = ViewModel.CurrentFont;
        if (font is null || XamlRoot is null || PreviewTextBox is null)
            return;

        ApplyFontToElement(PreviewTextBox, font.Metadata);
        ApplyPreviewBackground(_settings.PreviewBackground);
        BuildWaterfallView();
    }

    private void ApplyFontToElement(Control element, FontMetadata meta)
    {
        if (ViewModel.LoadedFontFamily != null)
        {
            element.FontFamily = ViewModel.LoadedFontFamily;
        }

        element.FontWeight = new FontWeight((ushort)meta.Weight);
        element.FontStyle = meta.IsItalic ? FontStyle.Italic
            : meta.IsOblique ? FontStyle.Oblique
            : FontStyle.Normal;
    }

    private void ApplyFontToTextBlock(TextBlock tb, FontMetadata meta)
    {
        if (ViewModel.LoadedFontFamily != null)
        {
            tb.FontFamily = ViewModel.LoadedFontFamily;
        }

        tb.FontWeight = new FontWeight((ushort)meta.Weight);
        tb.FontStyle = meta.IsItalic ? FontStyle.Italic
            : meta.IsOblique ? FontStyle.Oblique
            : FontStyle.Normal;
    }

    private void FontSizeUpButton_Click(object sender, RoutedEventArgs e)
    {
        var newSize = Math.Min(PreviewTextBox.FontSize + 2, 120);
        ViewModel.PreviewFontSize = newSize;
        BuildWaterfallView();
    }

    private void FontSizeDownButton_Click(object sender, RoutedEventArgs e)
    {
        var newSize = Math.Max(PreviewTextBox.FontSize - 2, 8);
        ViewModel.PreviewFontSize = newSize;
        BuildWaterfallView();
    }

    private void BuildWaterfallView()
    {
        WaterfallPanel.Children.Clear();
        if (!_settings.ShowWaterfall) return;

        var meta = ViewModel.CurrentFont?.Metadata;
        if (meta == null) return;

        var sizes = _settings.GetWaterfallSizes();
        var text = string.IsNullOrWhiteSpace(ViewModel.PreviewText)
            ? "The quick brown fox jumps over the lazy dog"
            : ViewModel.PreviewText;

        var previewBgMode = _settings.PreviewBackground;
        Brush? customTbBrush = null;
        Brush? customLabelBrush = null;

        if (previewBgMode == 1) // Light
        {
            customTbBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 18, 18, 18));
            customLabelBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 100, 100));
        }
        else if (previewBgMode == 2) // Dark
        {
            customTbBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 245, 245));
            customLabelBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 170, 170, 170));
        }

        foreach (var size in sizes)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var sizeLabel = new TextBlock
            {
                Text = $"{size}",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = customLabelBrush ?? ResolveThemeBrush("TextFillColorTertiaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(sizeLabel, 0);

            var tb = new TextBlock
            {
                Text = text,
                FontSize = size,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsTextSelectionEnabled = true,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (customTbBrush != null)
            {
                tb.Foreground = customTbBrush;
            }
            ApplyFontToTextBlock(tb, meta);
            Grid.SetColumn(tb, 1);

            row.Children.Add(sizeLabel);
            row.Children.Add(tb);
            WaterfallPanel.Children.Add(row);
        }
    }

    private void ApplyPreviewBackground(int mode)
    {
        if (PreviewSurfaceBorder == null) return;

        ApplyPreviewSurfaceAppearance(PreviewSurfaceBorder, mode);

        if (mode == 1)
        {
            if (PreviewSurfaceBorder.Child is FrameworkElement child)
                child.RequestedTheme = ElementTheme.Light;

            var darkText = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 18, 18, 18));
            PreviewTextBox.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            PreviewTextBox.Foreground = darkText;
        }
        else if (mode == 2)
        {
            if (PreviewSurfaceBorder.Child is FrameworkElement child)
                child.RequestedTheme = ElementTheme.Dark;

            var lightText = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 245, 245));
            PreviewTextBox.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            PreviewTextBox.Foreground = lightText;
        }
        else
        {
            if (PreviewSurfaceBorder.Child is FrameworkElement child)
                child.RequestedTheme = ElementTheme.Default;

            PreviewTextBox.ClearValue(TextBox.BackgroundProperty);
            PreviewTextBox.ClearValue(TextBox.ForegroundProperty);
        }
    }

    private static void ApplyPreviewSurfaceAppearance(Border border, int mode)
    {
        if (mode == 1)
        {
            border.RequestedTheme = ElementTheme.Light;
            border.Background = new SolidColorBrush(Microsoft.UI.Colors.White);
            border.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 224, 224, 224));
            border.BorderThickness = new Thickness(1);
            border.CornerRadius = new CornerRadius(8);
        }
        else if (mode == 2)
        {
            border.RequestedTheme = ElementTheme.Dark;
            border.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 26, 26));
            border.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 48, 48, 48));
            border.BorderThickness = new Thickness(1);
            border.CornerRadius = new CornerRadius(8);
        }
        else
        {
            border.RequestedTheme = ElementTheme.Default;
            border.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            border.BorderBrush = null;
            border.BorderThickness = new Thickness(0);
            border.ClearValue(Border.CornerRadiusProperty);
        }
    }

    private Brush ResolveThemeBrush(string resourceKey, Windows.UI.Color? fallback = null)
    {
        var color = resourceKey is "TextFillColorTertiaryBrush" or "TextFillColorSecondaryBrush"
            or "CardBackgroundFillColorDefaultBrush" or "CardStrokeColorDefaultBrush"
            ? AppThemeHelper.ThemeColor(resourceKey, IsApplicationLightTheme())
            : fallback ?? AppThemeHelper.ThemeColor(resourceKey, IsApplicationLightTheme());

        return new SolidColorBrush(color);
    }

    private bool IsApplicationLightTheme() =>
        AppThemeHelper.IsLightTheme(_settings.Theme, this);
}
