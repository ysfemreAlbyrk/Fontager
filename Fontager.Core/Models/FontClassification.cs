namespace Fontager.Core.Models;

/// <summary>
/// Font classification based on visual style.
/// </summary>
[Flags]
public enum FontClassification
{
    None = 0,
    Serif = 1,
    SansSerif = 2,
    Monospace = 4,
    Display = 8,
    Script = 16,
    Symbol = 32
}
