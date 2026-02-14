using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Fontager.Core.Models;
using Fontager.Core.Services;
using Fontager.Viewer.Services;
using Fontager.Viewer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Fontager.Viewer;

/// <summary>
/// Main window for the Fontager Viewer application.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly FontViewerViewModel _viewModel;
    private readonly SettingsService _settings;
    private FontFamily? _loadedFontFamily;

    // Win32 interop for loading private fonts
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int AddFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool RemoveFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

    private const uint FR_PRIVATE = 0x10;

    private string? _activeFontPath;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = App.Services.GetRequiredService<FontViewerViewModel>();
        _settings = App.Services.GetRequiredService<SettingsService>();

        // Configure window
        ConfigureWindow();

        // Apply saved settings
        ApplySettings();

        // Set up drag & drop
        RootGrid.AllowDrop = true;
        RootGrid.DragOver += RootGrid_DragOver;
        RootGrid.Drop += RootGrid_Drop;

        // Load font from command-line args if provided
        if (!string.IsNullOrEmpty(App.FontFilePath))
        {
            _ = LoadFontFromPathAsync(App.FontFilePath);
        }
    }

    private void ConfigureWindow()
    {
        // Set window size
        var appWindow = this.AppWindow;
        appWindow.Resize(new Windows.Graphics.SizeInt32(900, 700));

        // Set title
        appWindow.Title = "Fontager";

        // Extend content into title bar
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }

    // ── File Open ──────────────────────────────────────────────

    private async void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();

        // Initialize with window handle
        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);

        picker.ViewMode = PickerViewMode.List;
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".ttf");
        picker.FileTypeFilter.Add(".otf");
        picker.FileTypeFilter.Add(".ttc");
        picker.FileTypeFilter.Add(".woff2");

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            await LoadFontFromPathAsync(file.Path);
        }
    }

    // ── Drag & Drop ────────────────────────────────────────────

    private void RootGrid_DragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Open font";
        e.DragUIOverride.IsCaptionVisible = true;
    }

    private async void RootGrid_Drop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.Count > 0)
            {
                var file = items[0] as Windows.Storage.StorageFile;
                if (file != null)
                {
                    var fontService = App.Services.GetRequiredService<IFontService>();
                    if (fontService.IsSupportedFont(file.Path))
                    {
                        await LoadFontFromPathAsync(file.Path);
                    }
                }
            }
        }
    }

    // ── Font Loading ───────────────────────────────────────────

    private async Task LoadFontFromPathAsync(string filePath)
    {
        // Show loading state
        ShowState(loading: true);

        try
        {
            // Load font via ViewModel
            await _viewModel.LoadFontAsync(filePath);

            if (_viewModel.HasError)
            {
                ErrorText.Text = _viewModel.ErrorMessage;
                ShowState(error: true);
                return;
            }

            if (!_viewModel.HasFont || _viewModel.CurrentFont is null)
            {
                ErrorText.Text = "Failed to load font file.";
                ShowState(error: true);
                return;
            }

            // Remove previously activated private font
            DeactivateCurrentFont();

            // Activate the font privately using GDI
            AddFontResourceEx(filePath, FR_PRIVATE, IntPtr.Zero);
            _activeFontPath = filePath;

            // Create FontFamily from the font's family name
            var familyName = _viewModel.CurrentFont.Metadata.FamilyName;
            if (!string.IsNullOrWhiteSpace(familyName))
            {
                _loadedFontFamily = new FontFamily(familyName);
            }
            else
            {
                _loadedFontFamily = new FontFamily(Path.GetFileNameWithoutExtension(filePath));
            }

            // Update UI
            UpdateFontDisplay();
            ShowState(content: true);
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"Error: {ex.Message}";
            ShowState(error: true);
        }
    }

    private void DeactivateCurrentFont()
    {
        if (_activeFontPath != null)
        {
            RemoveFontResourceEx(_activeFontPath, FR_PRIVATE, IntPtr.Zero);
            _activeFontPath = null;
        }
    }

    private void UpdateFontDisplay()
    {
        var font = _viewModel.CurrentFont;
        if (font is null) return;

        var meta = font.Metadata;

        // Title bar
        TitleBarFontName.Text = font.DisplayName;
        AppWindow.Title = $"Fontager \u2014 {font.DisplayName}";

        // Header
        FontFamilyName.Text = meta.FamilyName;
        FontStyleName.Text = meta.SubfamilyName;
        FormatBadgeText.Text = font.Format.ToString();
        VariableBadge.Visibility = meta.IsVariable ? Visibility.Visible : Visibility.Collapsed;
        FontFileSize.Text = font.FormattedFileSize;

        // Apply font to preview
        if (_loadedFontFamily != null)
        {
            PreviewTextBlock.FontFamily = _loadedFontFamily;

            // Apply weight
            PreviewTextBlock.FontWeight = new Windows.UI.Text.FontWeight((ushort)meta.Weight);
            if (meta.IsItalic)
                PreviewTextBlock.FontStyle = Windows.UI.Text.FontStyle.Italic;
            else if (meta.IsOblique)
                PreviewTextBlock.FontStyle = Windows.UI.Text.FontStyle.Oblique;
            else
                PreviewTextBlock.FontStyle = Windows.UI.Text.FontStyle.Normal;
        }

        // Build waterfall
        BuildWaterfallView();

        // Build glyph grid
        BuildGlyphGrid();

        // Build metadata
        BuildMetadataView();
    }

    // ── Preview ────────────────────────────────────────────────

    private void PreviewTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        PreviewTextBlock.Text = PreviewTextBox.Text;
        _viewModel.PreviewText = PreviewTextBox.Text;

        // Update waterfall too
        if (_viewModel.HasFont)
        {
            BuildWaterfallView();
        }
    }

    private void FontSizeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (PreviewTextBlock != null)
        {
            PreviewTextBlock.FontSize = e.NewValue;
            if (FontSizeLabel != null)
            {
                FontSizeLabel.Text = $"{(int)e.NewValue}px";
            }
        }
    }

    // ── Waterfall ──────────────────────────────────────────────

    private void BuildWaterfallView()
    {
        WaterfallPanel.Children.Clear();

        int[] sizes = [8, 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 40, 48, 56, 64, 72];
        var text = string.IsNullOrWhiteSpace(_viewModel.PreviewText)
            ? "The quick brown fox jumps over the lazy dog"
            : _viewModel.PreviewText;

        foreach (var size in sizes)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var sizeLabel = new TextBlock
            {
                Text = $"{size}px",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 16, 0)
            };
            Grid.SetColumn(sizeLabel, 0);

            var textBlock = new TextBlock
            {
                Text = text,
                FontSize = size,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsTextSelectionEnabled = true,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (_loadedFontFamily != null)
            {
                textBlock.FontFamily = _loadedFontFamily;
            }

            Grid.SetColumn(textBlock, 1);

            row.Children.Add(sizeLabel);
            row.Children.Add(textBlock);

            WaterfallPanel.Children.Add(row);
        }
    }

    // ── Glyph Grid ─────────────────────────────────────────────

    private void BuildGlyphGrid()
    {
        _viewModel.GenerateGlyphItemsPublic();

        GlyphGrid.ItemsSource = _viewModel.GlyphItems;
        GlyphCountText.Text = _viewModel.CurrentFont?.Metadata.GlyphCount.ToString() ?? "0";

        // Apply font to glyph items via ContainerContentChanging event
        GlyphGrid.ContainerContentChanging -= GlyphGrid_ContainerContentChanging;
        GlyphGrid.ContainerContentChanging += GlyphGrid_ContainerContentChanging;
    }

    private void GlyphGrid_ContainerContentChanging(ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.Phase == 0 && _loadedFontFamily != null)
        {
            // Find the character TextBlock in the template and apply font
            args.RegisterUpdateCallback((s, a) =>
            {
                if (a.ItemContainer.ContentTemplateRoot is StackPanel panel &&
                    panel.Children.Count > 0 &&
                    panel.Children[0] is TextBlock charBlock)
                {
                    charBlock.FontFamily = _loadedFontFamily;
                }
            });
        }
    }

    private void GlyphGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GlyphGrid.SelectedItem is GlyphItem glyph)
        {
            GlyphDetailPanel.Visibility = Visibility.Visible;
            SelectedGlyphChar.Text = glyph.Character;
            SelectedGlyphUnicode.Text = glyph.UnicodeLabel;
            SelectedGlyphName.Text = $"Decimal: {glyph.CodePoint} | Character: {glyph.Character}";

            if (_loadedFontFamily != null)
            {
                SelectedGlyphChar.FontFamily = _loadedFontFamily;
            }
        }
        else
        {
            GlyphDetailPanel.Visibility = Visibility.Collapsed;
        }
    }

    // ── Metadata ───────────────────────────────────────────────

    private void BuildMetadataView()
    {
        MetadataPanel.Children.Clear();

        var font = _viewModel.CurrentFont;
        if (font is null) return;

        var meta = font.Metadata;

        AddMetadataSection("General");
        AddMetadataRow("Family Name", meta.FamilyName);
        AddMetadataRow("Subfamily", meta.SubfamilyName);
        AddMetadataRow("Full Name", meta.FullName);
        AddMetadataRow("PostScript Name", meta.PostScriptName);
        AddMetadataRow("Version", meta.Version);
        AddMetadataRow("Format", font.Format.ToString());
        AddMetadataRow("File Size", font.FormattedFileSize);
        AddMetadataRow("File Path", font.FilePath);

        AddMetadataSection("Metrics");
        AddMetadataRow("Glyphs", meta.GlyphCount.ToString());
        AddMetadataRow("Units per Em", meta.UnitsPerEm.ToString());
        AddMetadataRow("Weight", GetWeightName(meta.Weight));
        AddMetadataRow("Style", meta.IsItalic ? "Italic" : meta.IsOblique ? "Oblique" : "Normal");
        AddMetadataRow("Variable Font", meta.IsVariable ? "Yes" : "No");
        AddMetadataRow("Classification", meta.Classification.ToString());

        AddMetadataSection("Credits");
        AddMetadataRow("Designer", meta.Designer);
        AddMetadataRow("Vendor", meta.Vendor);
        AddMetadataRow("Copyright", meta.Copyright);
        AddMetadataRow("Trademark", meta.Trademark);

        AddMetadataSection("License");
        AddMetadataRow("License", meta.License);
        AddMetadataRow("License URL", meta.LicenseUrl);

        if (!string.IsNullOrWhiteSpace(meta.Description))
        {
            AddMetadataSection("Description");
            AddMetadataRow("", meta.Description);
        }
    }

    private void AddMetadataSection(string title)
    {
        var header = new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            Margin = new Thickness(0, 16, 0, 8)
        };
        MetadataPanel.Children.Add(header);
    }

    private void AddMetadataRow(string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        var border = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 1, 0, 1)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (!string.IsNullOrWhiteSpace(label))
        {
            var labelBlock = new TextBlock
            {
                Text = label,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(labelBlock, 0);
            grid.Children.Add(labelBlock);
        }

        var valueBlock = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.WrapWholeWords,
            IsTextSelectionEnabled = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(valueBlock, string.IsNullOrWhiteSpace(label) ? 0 : 1);
        if (string.IsNullOrWhiteSpace(label))
        {
            Grid.SetColumnSpan(valueBlock, 2);
        }
        grid.Children.Add(valueBlock);

        border.Child = grid;
        MetadataPanel.Children.Add(border);
    }

    // ── State Management ───────────────────────────────────────

    private void ShowState(bool empty = false, bool loading = false, bool error = false, bool content = false)
    {
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        LoadingState.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        ErrorState.Visibility = error ? Visibility.Visible : Visibility.Collapsed;
        FontContent.Visibility = content ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Settings ────────────────────────────────────────────────

    private void ApplySettings()
    {
        // Apply theme
        if (RootGrid.XamlRoot != null)
        {
            ((FrameworkElement)Content).RequestedTheme = _settings.Theme;
        }
        else
        {
            RootGrid.Loaded += (_, _) =>
            {
                ((FrameworkElement)Content).RequestedTheme = _settings.Theme;
            };
        }

        // Apply default preview text and font size
        PreviewTextBox.Text = _settings.DefaultPreviewText;
        _viewModel.PreviewText = _settings.DefaultPreviewText;
        FontSizeSlider.Value = _settings.DefaultFontSize;
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        // Build settings dialog
        var themeCombo = new ComboBox
        {
            Header = "Theme",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new ComboBoxItem { Content = "System default", Tag = ElementTheme.Default },
                new ComboBoxItem { Content = "Light", Tag = ElementTheme.Light },
                new ComboBoxItem { Content = "Dark", Tag = ElementTheme.Dark }
            },
            SelectedIndex = (int)_settings.Theme
        };

        var previewTextBox = new TextBox
        {
            Header = "Default preview text",
            Text = _settings.DefaultPreviewText,
            PlaceholderText = "Enter default preview text...",
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = false,
            MaxLength = 500
        };

        var fontSizeSlider = new Slider
        {
            Header = $"Default font size ({(int)_settings.DefaultFontSize}px)",
            Minimum = 8,
            Maximum = 120,
            Value = _settings.DefaultFontSize,
            StepFrequency = 1
        };
        fontSizeSlider.ValueChanged += (s, args) =>
        {
            fontSizeSlider.Header = $"Default font size ({(int)args.NewValue}px)";
        };

        var panel = new StackPanel { Spacing = 16, MinWidth = 360 };
        panel.Children.Add(themeCombo);
        panel.Children.Add(previewTextBox);
        panel.Children.Add(fontSizeSlider);

        var dialog = new ContentDialog
        {
            Title = "Settings",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            // Save settings
            if (themeCombo.SelectedItem is ComboBoxItem selectedTheme && selectedTheme.Tag is ElementTheme theme)
            {
                _settings.Theme = theme;
                ((FrameworkElement)Content).RequestedTheme = theme;
            }

            _settings.DefaultPreviewText = previewTextBox.Text;
            _settings.DefaultFontSize = fontSizeSlider.Value;

            // Apply to current view if no font is loaded yet
            if (!_viewModel.HasFont)
            {
                PreviewTextBox.Text = _settings.DefaultPreviewText;
                FontSizeSlider.Value = _settings.DefaultFontSize;
            }
        }
    }

    // ── Helpers ─────────────────────────────────────────────────

    private static string GetWeightName(int weight) => weight switch
    {
        100 => "Thin (100)",
        200 => "ExtraLight (200)",
        300 => "Light (300)",
        400 => "Regular (400)",
        500 => "Medium (500)",
        600 => "SemiBold (600)",
        700 => "Bold (700)",
        800 => "ExtraBold (800)",
        900 => "Black (900)",
        _ => $"Weight ({weight})"
    };
}
