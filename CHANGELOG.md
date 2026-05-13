# Changelog

## [Unreleased]

### 📚 Docs
- Added `docs/research/font-parsing.md` — comparison of `SixLabors.Fonts`, `HarfBuzzSharp`/`SkiaSharp`, `Typography`, `Win2D` + DirectWrite, and `SharpFont` for fixing WOFF2 metadata and cmap-aware glyph enumeration; includes a TTF file-association appendix.

---

## [1.0.0]

🎉 First Release

### ➕ Added
- About section in settings — version, product info, author, GitHub link at the bottom

### 🔄 Changed
- Version now read from `Package.appxmanifest` instead of assembly
- New app icon — custom Logo.png (F + magnifying glass) in title bar and empty state
- Widened settings dialog (ContentDialogMaxWidth 640)

---

## [0.0.5-alpha]

### ➕ Added
- Enhanced version retrieval in MainWindow.xaml.cs to prioritize package version

### 🔄 Changed
- Modified app.manifest to remove assembly identity
- Changed GenerateAppInstallerFile to False in Fontager.Viewer.csproj
- Enhanced file associations and UI consistency
- Improved project documentation structure
- Enhanced build instructions for MSIX packaging
- Updated App.xaml for better file activation handling

---

## [0.0.3-alpha]

### ➕ Added
- File activation support — double-clicking .ttc/.otf/.woff2 opens the file when Fontager is set as default
- Quick View feature for character set overview in font header
- Toggle in settings to show/hide Quick View
- Font caching in LocalFolder for improved performance
- Settings service for waterfall sizes management
- Typographic family name support in FontParser
- Enhanced font resolution logic for XAML FontFamily handling
- Enhanced README.md with build instructions

### 🔄 Changed
- Added AppPackages and *.pfx to .gitignore
- Refactored font installation UI - moved install button to header
- Updated preview area to use single editable TextBox
- Improved preview section UI margins and padding
- Enhanced UI layout with auto-show/hide Quick View based on window size
- Updated app manifest version and file type associations
- Improved project properties for better packaging

### 🐛 Fixed
- Multi-font support implementation
- Font family name resolution for better XAML integration

---

## [0.0.1-alpha] - 2025-02-12

### ➕ Added
- Initial project setup and core architecture
- Modern WinUI 3 interface with Fluent Design
- Support for TTF, OTF, TTC, and WOFF2 font formats
- Instant font preview with editable text
- Waterfall view for multiple size preview
- Glyph grid with Unicode code points
- Font metadata display (family, designer, license, version)
- Multi-font support for TrueType Collections
- Drag & drop font loading
- Font installation for current user or all users
- Configurable themes and display options
- File association for font formats

### 🔧 Technical
- Custom binary font parser (no external dependencies)
- MVVM architecture with CommunityToolkit.Mvvm
- Dependency injection with Microsoft.Extensions.DependencyInjection
- Win32 GDI integration for font loading
- MSIX packaging for Windows Store deployment

### ⚠️ Known Issues
- Some exotic font formats may not be fully supported
- Performance with very large font collections needs optimization
- Font Manager module is still in development

---

## [Future Plans]

### 🚀 Planned Features

#### **Fontager.Viewer**
- Font compare
- Copying glyphs
- Dark and Light background preview
- Recent Files (for blank screen)

#### **Fontager.Manager**
- Professional font management suite
- Google Fonts integration
- Font collections and tagging
- Temporary font activation
- Font library organization
- Font comparison tools
- Export font catalogs
