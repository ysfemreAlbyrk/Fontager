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
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Fontager.Viewer;

public sealed partial class MainWindow : Window
{
    private readonly FontViewerViewModel _viewModel;
    private readonly SettingsService _settings;
    private readonly IFontService _fontService;
    private FontFamily? _loadedFontFamily;

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int AddFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool RemoveFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

    private const uint FR_PRIVATE = 0x10;

    // Subfolder inside LocalFolder where we cache fonts for XAML rendering
    private const string FontCacheFolderName = "FontCache";

    private string? _activeFontPath;
    private string? _currentFilePath;
    private int _currentFontIndex;
    private int _currentFontCount = 1;
    private string? _cachedFontFileName;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = App.Services.GetRequiredService<FontViewerViewModel>();
        _settings = App.Services.GetRequiredService<SettingsService>();
        _fontService = App.Services.GetRequiredService<IFontService>();

        ConfigureWindow();
        ApplySettings();
        ApplyBackdrop();

        RootGrid.AllowDrop = true;
        RootGrid.DragOver += RootGrid_DragOver;
        RootGrid.Drop += RootGrid_Drop;

        if (!string.IsNullOrEmpty(App.FontFilePath))
        {
            _ = LoadFontFromPathAsync(App.FontFilePath, 0);
        }
    }

    private void ConfigureWindow()
    {
        var appWindow = this.AppWindow;
        appWindow.Resize(new Windows.Graphics.SizeInt32(1300, 750));
        appWindow.Title = "Fontager";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }

    private void ApplyBackdrop()
    {
        if (_settings.Backdrop == 1)
            SystemBackdrop = new DesktopAcrylicBackdrop();
        else
            SystemBackdrop = new MicaBackdrop();
    }

    // ── File Open ──────────────────────────────────────────────

    private async void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
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
            await LoadFontFromPathAsync(file.Path, 0);
        }
    }

    // ── Install ────────────────────────────────────────────────

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFilePath == null) return;

        try
        {
            var userFontsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Windows", "Fonts");

            Directory.CreateDirectory(userFontsDir);

            var destPath = Path.Combine(userFontsDir, Path.GetFileName(_currentFilePath));

            if (File.Exists(destPath))
            {
                var overwriteDialog = new ContentDialog
                {
                    Title = "Font Already Installed",
                    Content = "This font is already installed. Do you want to overwrite it?",
                    PrimaryButtonText = "Overwrite",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = Content.XamlRoot
                };
                var overwriteResult = await overwriteDialog.ShowAsync();
                if (overwriteResult != ContentDialogResult.Primary)
                    return;
            }

            File.Copy(_currentFilePath, destPath, true);

            var regKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts", true);

            if (regKey != null)
            {
                var fontName = _viewModel.CurrentFont?.DisplayName ?? Path.GetFileNameWithoutExtension(_currentFilePath);
                regKey.SetValue(fontName, destPath);
                regKey.Close();
            }

            var dialog = new ContentDialog
            {
                Title = "Font Installed",
                Content = $"'{_viewModel.CurrentFont?.DisplayName ?? "Font"}' has been installed for the current user.",
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "Installation Failed",
                Content = $"Could not install font: {ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
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
            if (items.Count > 0 && items[0] is Windows.Storage.StorageFile file)
            {
                if (_fontService.IsSupportedFont(file.Path))
                {
                    await LoadFontFromPathAsync(file.Path, 0);
                }
            }
        }
    }

    // ── Multi-Font Navigation ──────────────────────────────────

    private async void PrevFontButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFilePath != null && _currentFontIndex > 0)
            await LoadFontFromPathAsync(_currentFilePath, _currentFontIndex - 1);
    }

    private async void NextFontButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFilePath != null && _currentFontIndex < _currentFontCount - 1)
            await LoadFontFromPathAsync(_currentFilePath, _currentFontIndex + 1);
    }

    // ── Font Loading ───────────────────────────────────────────

    private async Task LoadFontFromPathAsync(string filePath, int fontIndex)
    {
        ShowState(loading: true);

        try
        {
            await _viewModel.LoadFontAsync(filePath, fontIndex);

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

            DeactivateCurrentFont();

            // Also register via GDI for any non-XAML rendering paths
            AddFontResourceEx(filePath, FR_PRIVATE, IntPtr.Zero);
            _activeFontPath = filePath;
            _currentFilePath = filePath;
            _currentFontIndex = _viewModel.CurrentFont.FontIndex;
            _currentFontCount = _viewModel.CurrentFont.FontCount;

            // WinUI 3 XAML FontFamily does NOT support file:/// or absolute paths.
            // It only supports ms-appx:/// and ms-appdata:///local/ URI schemes.
            // Solution: copy the font to LocalFolder/FontCache/ and reference it
            // via ms-appdata:///local/FontCache/filename.ttf#FamilyName.
            // This is fast (milliseconds) and the cache is cleaned on each load.
            var familyName = _viewModel.CurrentFont.Metadata.FamilyName;
            if (string.IsNullOrWhiteSpace(familyName))
                familyName = Path.GetFileNameWithoutExtension(filePath);

            var cachedUri = await CacheFontForXamlAsync(filePath);
            _loadedFontFamily = new FontFamily($"{cachedUri}#{familyName}");

            UpdateFontDisplay();
            ShowState(content: true);
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"Error: {ex.Message}";
            ShowState(error: true);
        }
    }

    /// <summary>
    /// Copies the font file into LocalFolder/FontCache/ so that XAML can load it
    /// via the ms-appdata:///local/ URI scheme. Returns the ms-appdata URI (without #name).
    /// </summary>
    private async Task<string> CacheFontForXamlAsync(string sourceFilePath)
    {
        var localFolder = ApplicationData.Current.LocalFolder;
        var cacheFolder = await localFolder.CreateFolderAsync(
            FontCacheFolderName, CreationCollisionOption.OpenIfExists);

        // Clean previous cached font to avoid stale files
        if (_cachedFontFileName != null)
        {
            try
            {
                var oldFile = await cacheFolder.TryGetItemAsync(_cachedFontFileName);
                if (oldFile is StorageFile sf)
                    await sf.DeleteAsync(StorageDeleteOption.PermanentDelete);
            }
            catch { /* ignore cleanup errors */ }
        }

        // Use a unique name to avoid collisions: GUID + original extension
        var ext = Path.GetExtension(sourceFilePath);
        var uniqueName = $"{Guid.NewGuid():N}{ext}";

        var sourceFile = await StorageFile.GetFileFromPathAsync(sourceFilePath);
        await sourceFile.CopyAsync(cacheFolder, uniqueName, NameCollisionOption.ReplaceExisting);

        _cachedFontFileName = uniqueName;

        // ms-appdata:///local/ maps to ApplicationData.Current.LocalFolder
        return $"ms-appdata:///local/{FontCacheFolderName}/{uniqueName}";
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

        TitleBarFontName.Text = font.DisplayName;
        AppWindow.Title = $"Fontager \u2014 {font.DisplayName}";

        FontFamilyName.Text = meta.FamilyName;
        FontStyleName.Text = meta.SubfamilyName;
        FormatBadgeText.Text = font.Format.ToString();
        VariableBadge.Visibility = meta.IsVariable ? Visibility.Visible : Visibility.Collapsed;
        FontFileSize.Text = font.FormattedFileSize;

        // Multi-font navigation
        if (font.FontCount > 1)
        {
            FontNavPanel.Visibility = Visibility.Visible;
            FontIndexLabel.Text = $"{font.FontIndex + 1} / {font.FontCount}";
            PrevFontButton.IsEnabled = font.FontIndex > 0;
            NextFontButton.IsEnabled = font.FontIndex < font.FontCount - 1;
        }
        else
        {
            FontNavPanel.Visibility = Visibility.Collapsed;
        }

        // Apply font to the unified preview TextBox
        if (_loadedFontFamily != null)
        {
            PreviewTextBox.FontFamily = _loadedFontFamily;
            PreviewTextBox.FontWeight = new Windows.UI.Text.FontWeight((ushort)meta.Weight);

            if (meta.IsItalic)
                PreviewTextBox.FontStyle = Windows.UI.Text.FontStyle.Italic;
            else if (meta.IsOblique)
                PreviewTextBox.FontStyle = Windows.UI.Text.FontStyle.Oblique;
            else
                PreviewTextBox.FontStyle = Windows.UI.Text.FontStyle.Normal;
        }

        // Quick View
        QuickViewSection.Visibility = _settings.ShowQuickView ? Visibility.Visible : Visibility.Collapsed;
        BuildQuickView();

        WaterfallSection.Visibility = _settings.ShowWaterfall ? Visibility.Visible : Visibility.Collapsed;
        BuildWaterfallView();
        BuildGlyphGrid();
        BuildMetadataView();
    }

    // ── Quick View ─────────────────────────────────────────────

    private void BuildQuickView()
    {
        QuickViewPanel.Children.Clear();

        if (!_settings.ShowQuickView) return;

        // Standard character set lines like Windows Font Viewer
        string[] lines =
        [
            "abcdefghijklmnopqrstuvwxyz",
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ",
            "1234567890.:,;'\"(!?) +-*/="
        ];

        var meta = _viewModel.CurrentFont?.Metadata;

        foreach (var line in lines)
        {
            var textBlock = new TextBlock
            {
                Text = line,
                FontSize = 24,
                TextWrapping = TextWrapping.WrapWholeWords,
                IsTextSelectionEnabled = true,
                Margin = new Thickness(0, 2, 0, 2)
            };

            if (_loadedFontFamily != null)
                textBlock.FontFamily = _loadedFontFamily;

            if (meta != null)
            {
                textBlock.FontWeight = new Windows.UI.Text.FontWeight((ushort)meta.Weight);

                if (meta.IsItalic)
                    textBlock.FontStyle = Windows.UI.Text.FontStyle.Italic;
                else if (meta.IsOblique)
                    textBlock.FontStyle = Windows.UI.Text.FontStyle.Oblique;
                else
                    textBlock.FontStyle = Windows.UI.Text.FontStyle.Normal;
            }

            QuickViewPanel.Children.Add(textBlock);
        }
    }

    // ── Preview ────────────────────────────────────────────────

    private void PreviewTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _viewModel.PreviewText = PreviewTextBox.Text;

        if (_viewModel.HasFont)
            BuildWaterfallView();
    }

    private void FontSizeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (PreviewTextBox != null)
        {
            PreviewTextBox.FontSize = e.NewValue;
            if (FontSizeLabel != null)
                FontSizeLabel.Text = $"{(int)e.NewValue}px";
        }
    }

    // ── Waterfall ──────────────────────────────────────────────

    private void BuildWaterfallView()
    {
        WaterfallPanel.Children.Clear();

        if (!_settings.ShowWaterfall) return;

        var sizes = _settings.GetWaterfallSizes();
        var text = string.IsNullOrWhiteSpace(_viewModel.PreviewText)
            ? "The quick brown fox jumps over the lazy dog"
            : _viewModel.PreviewText;

        var meta = _viewModel.CurrentFont?.Metadata;

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

            // Apply font family, weight, and style to waterfall items
            if (_loadedFontFamily != null)
                textBlock.FontFamily = _loadedFontFamily;

            if (meta != null)
            {
                textBlock.FontWeight = new Windows.UI.Text.FontWeight((ushort)meta.Weight);

                if (meta.IsItalic)
                    textBlock.FontStyle = Windows.UI.Text.FontStyle.Italic;
                else if (meta.IsOblique)
                    textBlock.FontStyle = Windows.UI.Text.FontStyle.Oblique;
                else
                    textBlock.FontStyle = Windows.UI.Text.FontStyle.Normal;
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

        GlyphGrid.ContainerContentChanging -= GlyphGrid_ContainerContentChanging;
        GlyphGrid.ContainerContentChanging += GlyphGrid_ContainerContentChanging;
    }

    private void GlyphGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Phase == 0 && _loadedFontFamily != null)
        {
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
                SelectedGlyphChar.FontFamily = _loadedFontFamily;
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
        if (font.FontCount > 1)
            AddMetadataRow("Font in Collection", $"{font.FontIndex + 1} of {font.FontCount}");

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
        MetadataPanel.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            Margin = new Thickness(0, 16, 0, 8)
        });
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
            Grid.SetColumnSpan(valueBlock, 2);
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

        PreviewTextBox.Text = _settings.DefaultPreviewText;
        _viewModel.PreviewText = _settings.DefaultPreviewText;
        FontSizeSlider.Value = _settings.DefaultFontSize;
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
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

        var backdropCombo = new ComboBox
        {
            Header = "Backdrop",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new ComboBoxItem { Content = "Mica", Tag = 0 },
                new ComboBoxItem { Content = "Acrylic", Tag = 1 }
            },
            SelectedIndex = _settings.Backdrop
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

        var quickViewToggle = new ToggleSwitch
        {
            Header = "Show quick view (character set overview)",
            IsOn = _settings.ShowQuickView
        };

        var waterfallToggle = new ToggleSwitch
        {
            Header = "Show waterfall in Preview tab",
            IsOn = _settings.ShowWaterfall
        };

        var waterfallSizesBox = new TextBox
        {
            Header = "Waterfall sizes (comma-separated)",
            Text = _settings.WaterfallSizesRaw,
            PlaceholderText = "8,12,16,24,32,48,72",
            TextWrapping = TextWrapping.NoWrap,
            AcceptsReturn = false,
            MaxLength = 200
        };

        var panel = new StackPanel { Spacing = 16, MinWidth = 380 };
        panel.Children.Add(themeCombo);
        panel.Children.Add(backdropCombo);
        panel.Children.Add(previewTextBox);
        panel.Children.Add(fontSizeSlider);
        panel.Children.Add(quickViewToggle);
        panel.Children.Add(waterfallToggle);
        panel.Children.Add(waterfallSizesBox);

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
            if (themeCombo.SelectedItem is ComboBoxItem selectedTheme && selectedTheme.Tag is ElementTheme theme)
            {
                _settings.Theme = theme;
                ((FrameworkElement)Content).RequestedTheme = theme;
            }

            if (backdropCombo.SelectedItem is ComboBoxItem selectedBackdrop && selectedBackdrop.Tag is int backdropVal)
            {
                _settings.Backdrop = backdropVal;
                ApplyBackdrop();
            }

            _settings.DefaultPreviewText = previewTextBox.Text;
            _settings.DefaultFontSize = fontSizeSlider.Value;
            _settings.ShowQuickView = quickViewToggle.IsOn;
            _settings.ShowWaterfall = waterfallToggle.IsOn;
            _settings.WaterfallSizesRaw = waterfallSizesBox.Text;

            if (!_viewModel.HasFont)
            {
                PreviewTextBox.Text = _settings.DefaultPreviewText;
                FontSizeSlider.Value = _settings.DefaultFontSize;
            }

            if (_viewModel.HasFont)
            {
                // Refresh Quick View
                QuickViewSection.Visibility = _settings.ShowQuickView ? Visibility.Visible : Visibility.Collapsed;
                if (_settings.ShowQuickView)
                    BuildQuickView();

                // Refresh Waterfall
                WaterfallSection.Visibility = _settings.ShowWaterfall ? Visibility.Visible : Visibility.Collapsed;
                if (_settings.ShowWaterfall)
                    BuildWaterfallView();
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
