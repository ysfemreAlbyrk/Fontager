# Fontager

A modern powerful font manager and viewer for Windows, built with WinUI 3.

Fontager replaces the outdated Windows `fontview.exe` with a fast, beautiful font previewer and aims to provide a full-featured font management suite for designers and developers.

## Applications

### Fontager.Viewer

A lightweight font previewer that can be set as the default handler for font files.

**Features:**

- **Instant preview** of `.ttf`, `.otf`, `.ttc`, and `.woff2` files
- **Editable preview text** with adjustable font size
- **Quick View** — character set overview at a glance
- **Waterfall view** — preview at multiple sizes simultaneously
- **Glyph grid** — browse every character with Unicode code points
- **Font metadata** — family, designer, license, version, and more
- **Multi-font support** — navigate fonts within TrueType Collections (`.ttc`)
- **Drag & drop** — open fonts by dropping them onto the window
- **Font installation** — install for current user or all users
- **Modern UI** — Mica/Acrylic backdrop, Fluent Design, custom title bar
- **Configurable** — theme, backdrop, preview defaults, and display options

### Fontager.Manager *(planned)*

A professional-grade font management suite with library organization, temporary activation, collections, and Google Fonts integration.

### Fontager.Core

Shared library containing models, services, and helpers used by both applications:

- **FontParser** — binary parser for TTF/OTF/TTC metadata (name, OS/2, head, maxp, fvar tables)
- **FontService** — font loading, discovery, and format detection
- **Models** — `FontModel`, `FontMetadata`, `FontFormat`, `FontClassification`

## Requirements

- Windows 10 (19041+) or Windows 11
- .NET 8
- Windows App SDK 1.8+

## Building

1. Open `Fontager.sln` in Visual Studio 2022 (17.8+)
2. Ensure the **Windows App SDK** workload is installed
3. Set `Fontager.Viewer` as the startup project
4. Build and run (F5)

## Tech Stack

- **UI Framework:** WinUI 3 (Windows App SDK)
- **Language:** C# / .NET 8
- **Architecture:** MVVM with `CommunityToolkit.Mvvm`
- **DI:** `Microsoft.Extensions.DependencyInjection`
- **Font Parsing:** Custom binary parser (no external dependencies)
- **Font Loading:** Win32 GDI (`AddFontResourceEx`) + XAML `ms-appdata` URI caching

## File Association

Fontager.Viewer can register as the default handler for font files (`.ttf`, `.otf`, `.ttc`, `.woff2`). After installation, right-click any font file and select **Open with > Fontager Viewer**, or set it as the default in Windows Settings.

## License

This project is free and open-source software (FOSS). See [LICENSE](LICENSE) for details.

## Contributing

Contributions are welcome! Please open an issue to discuss proposed changes before submitting a pull request.
