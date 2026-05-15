using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Fontager.Viewer;

/// <summary>
/// Builds the right-hand "Info" tab content for <see cref="MainWindow"/>.
/// Each call to <see cref="BuildMetadataView"/> clears the panel and
/// re-renders the metadata for the currently-loaded font into themed
/// label/value cards.
///
/// <para>
/// Rendering is intentionally programmatic rather than DataTemplate-driven:
/// the rows we show depend on what <see cref="Fontager.Core.Models.FontMetadata"/>
/// actually populated (empty strings get skipped), and the section list
/// itself grows whenever we extract more from the font, so a runtime
/// build keeps the XAML stable.
/// </para>
/// </summary>
public sealed partial class MainWindow
{
    // ── Metadata ───────────────────────────────────────────────

    private void BuildMetadataView()
    {
        MetadataPanel.Children.Clear();
        var font = _viewModel.CurrentFont;
        if (font is null) return;
        var meta = font.Metadata;

        AddMetadataSection("General");
        AddMetadataRow("Family Name", meta.FamilyName);
        if (meta.TypographicFamilyName != meta.FamilyName)
            AddMetadataRow("Typographic Family", meta.TypographicFamilyName);
        AddMetadataRow("Subfamily", meta.SubfamilyName);
        AddMetadataRow("Full Name", meta.FullName);
        AddMetadataRow("PostScript Name", meta.PostScriptName);
        AddMetadataRow("Unique ID", meta.UniqueId);
        AddMetadataRow("Version", meta.Version);
        AddMetadataRow("Font Revision", meta.FontRevision);
        AddMetadataRow("Format", font.Format.ToString());
        AddMetadataRow("File Size", font.FormattedFileSize);
        AddMetadataRow("File Path", font.FilePath);
        if (font.FontCount > 1)
            AddMetadataRow("Font in Collection", $"{font.FontIndex + 1} of {font.FontCount}");
        AddMetadataRow("Created", meta.Created);
        AddMetadataRow("Modified", meta.Modified);

        AddMetadataSection("Style");
        AddMetadataRow("Weight", GetWeightName(meta.Weight));
        AddMetadataRow("Width", GetWidthName(meta.Width));
        AddMetadataRow("Style", meta.IsItalic ? "Italic" : meta.IsOblique ? "Oblique" : "Normal");
        AddMetadataRow("Fixed Pitch", meta.IsFixedPitch ? "Yes" : "No");
        AddMetadataRow("macStyle", meta.MacStyle);
        AddMetadataRow("Variable Font", meta.IsVariable ? "Yes" : "No");
        AddMetadataRow("Classification", meta.Classification.ToString());
        AddMetadataRow("PANOSE", meta.Panose);
        AddMetadataRow("Italic Angle", meta.ItalicAngle);

        AddMetadataSection("Metrics");
        AddMetadataRow("Glyphs", meta.GlyphCount.ToString());
        AddMetadataRow("Units per Em", meta.UnitsPerEm.ToString());
        if (meta.XMax != 0 || meta.YMax != 0 || meta.XMin != 0 || meta.YMin != 0)
            AddMetadataRow("Bounding Box", $"({meta.XMin}, {meta.YMin}) → ({meta.XMax}, {meta.YMax})");
        AddMetadataRow("Typo Ascender", FormatMetric(meta.TypoAscender));
        AddMetadataRow("Typo Descender", FormatMetric(meta.TypoDescender));
        AddMetadataRow("Typo Line Gap", FormatMetric(meta.TypoLineGap));
        AddMetadataRow("Win Ascent", FormatMetric(meta.WinAscent));
        AddMetadataRow("Win Descent", FormatMetric(meta.WinDescent));
        AddMetadataRow("hhea Ascender", FormatMetric(meta.HheaAscender));
        AddMetadataRow("hhea Descender", FormatMetric(meta.HheaDescender));
        AddMetadataRow("hhea Line Gap", FormatMetric(meta.HheaLineGap));
        AddMetadataRow("x-Height", FormatMetric(meta.XHeight));
        AddMetadataRow("Cap Height", FormatMetric(meta.CapHeight));
        AddMetadataRow("Underline Position", FormatMetric(meta.UnderlinePosition));
        AddMetadataRow("Underline Thickness", FormatMetric(meta.UnderlineThickness));

        if (meta.Axes.Count > 0)
        {
            AddMetadataSection("Variation Axes");
            foreach (var axis in meta.Axes)
            {
                AddMetadataRow(
                    $"{axis.Tag} ({axis.Name})",
                    $"min {axis.Min:0.##}, default {axis.Default:0.##}, max {axis.Max:0.##}");
            }
        }

        if (meta.GsubFeatures.Count > 0)
        {
            AddMetadataSection("OpenType Features — GSUB");
            AddMetadataRow($"{meta.GsubFeatures.Count} tags", string.Join(", ", meta.GsubFeatures));
        }
        if (meta.GposFeatures.Count > 0)
        {
            AddMetadataSection("OpenType Features — GPOS");
            AddMetadataRow($"{meta.GposFeatures.Count} tags", string.Join(", ", meta.GposFeatures));
        }

        AddMetadataSection("Credits");
        AddMetadataRow("Designer", meta.Designer);
        AddMetadataRow("Designer URL", meta.DesignerUrl);
        AddMetadataRow("Manufacturer", meta.Manufacturer);
        AddMetadataRow("Manufacturer URL", meta.ManufacturerUrl);
        AddMetadataRow("Vendor", meta.Vendor);
        AddMetadataRow("Copyright", meta.Copyright);
        AddMetadataRow("Trademark", meta.Trademark);

        AddMetadataSection("License");
        AddMetadataRow("License", meta.License);
        AddMetadataRow("License URL", meta.LicenseUrl);
        AddMetadataRow("Embedding", meta.EmbeddingRights);

        if (!string.IsNullOrWhiteSpace(meta.Description))
        {
            AddMetadataSection("Description");
            AddMetadataRow("", meta.Description);
        }
        if (!string.IsNullOrWhiteSpace(meta.SampleText))
        {
            AddMetadataSection("Sample Text");
            AddMetadataRow("", meta.SampleText);
        }
    }

    private static string FormatMetric(int value) => value == 0 ? string.Empty : value.ToString();

    private static string GetWidthName(int width) => width switch
    {
        1 => "Ultra-condensed (1)",
        2 => "Extra-condensed (2)",
        3 => "Condensed (3)",
        4 => "Semi-condensed (4)",
        5 => "Normal (5)",
        6 => "Semi-expanded (6)",
        7 => "Expanded (7)",
        8 => "Extra-expanded (8)",
        9 => "Ultra-expanded (9)",
        _ => $"Width ({width})"
    };

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
}
