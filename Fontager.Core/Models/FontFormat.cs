namespace Fontager.Core.Models;

/// <summary>
/// Supported font file formats.
/// </summary>
public enum FontFormat
{
    Unknown,
    TrueType,   // .ttf
    OpenType,    // .otf
    TrueTypeCollection, // .ttc
    WebOpenFont  // .woff2
}
