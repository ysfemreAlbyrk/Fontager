# Changelog

## [Unreleased]

### 🔄 Changed
- **Distribution switched to unpackaged WinUI 3** with the Windows App SDK runtime bundled self-contained. The MSIX manifest (`Package.appxmanifest`) stays in the repository so Store distribution can be re-enabled later as a single-property change. Rationale and tradeoffs are documented in `docs/research/packaging-decision.md`.
- Settings storage moved off `Windows.Storage.ApplicationData` and onto a plain JSON file at `%LocalAppData%\Fontager\settings.json`. The file is written atomically (temp + rename) so a mid-write power loss can't corrupt it. The new path is identical across packaging modes, so re-enabling MSIX later won't require a settings migration.
- `FileAssociationService` now registers the four supported font formats (`.ttf`, `.otf`, `.ttc`, `.woff2`) under one unified ProgID. The Settings dialog exposes a single "Register Fontager for font files (current user)" toggle covering all four. Legacy single-extension entries from older installs are cleaned up automatically.

### ⚡️ Performance
- Glyphs tab no longer freezes on CJK or emoji fonts with 10k+ glyphs. The root cause was UI virtualization being disabled — the `GridView` was nested inside a `ScrollViewer > StackPanel`, which handed it infinite height and forced every cell to materialize up-front. The GridView now owns its own scrolling and `ItemsWrapGrid` virtualizes off-screen rows.
- `GlyphItem` now precomputes its `Block` and `Category` once at construction so per-keystroke filtering no longer calls `GlyphCategoryClassifier.Classify(...)` thousands of times.
- Glyph search input is debounced to 150 ms so a fast typist doesn't rebuild the filtered grid on every keystroke.
- `FontFamily` is set once on the `GridView` (and inherits to per-cell `TextBlock`s) instead of being assigned per realized container as the user scrolls.
- `FontViewerViewModel.GlyphItems` is now a plain `List<T>` rather than `ObservableCollection<T>`: it's only ever read in bulk by code-driven filtering, so the change-notification machinery was pure overhead on every font load.

### ➕ Added
- Glyphs tab is now categorized. The grid is filtered along three orthogonal axes: a Unicode-block sidebar (only blocks the font actually covers, with per-block glyph counts), a functional category chip row (All, Uppercase, Lowercase, Numbers, Punctuation, Symbols, Accented, Other), and a search box that accepts a literal character, hex (`U+00A0`/`0x00A0`/`00A0`), or decimal code point. The glyph detail card now also shows the matching block and category.
- `FontParser` now reads the `cmap` table (subtable formats 0, 4, 6, 12) and exposes the actual supported Unicode code points via `FontMetadata.SupportedCodePoints`. Icon fonts, CJK fonts, and symbol fonts now show their real glyph coverage instead of an empty Latin grid.

### 🐛 Fixed
- "Install for current user" now actually shows up in Settings → Fonts without a logoff. After copying the font to `%LocalAppData%\Microsoft\Windows\Fonts` and writing `HKCU`, the app calls `AddFontResource` to register it in the current session and broadcasts `WM_FONTCHANGE` so the shell and the Font Cache service refresh immediately. The registry write is verified post-write and the user is told explicitly when the write is virtualized away by packaged identity.
- Settings → Fonts now offers an actual "Uninstall" button for fonts installed by Fontager (previously only "Hide" was available). Unblocked by the move off MSIX — under packaged identity our HKCU writes were virtualized into the package container and Windows treated the font as system-managed.
- Registry value names now use the `" (TrueType)"` / `" (OpenType)"` suffix Windows expects, so the Font Cache service stops silently rejecting the entry.
- Drag-and-drop and the "Open" file picker now work when Fontager is launched with "Run as administrator". `WM_DROPFILES`, `WM_COPYDATA`, and `WM_COPYGLOBALDATA` are whitelisted via `ChangeWindowMessageFilterEx` so lower-integrity Explorer can talk to the elevated window, and the WinRT `FileOpenPicker` (which fails under elevation) is replaced by a Win32 `IFileOpenDialog` in that case.
- Window now shows the Fontager logo in Alt+Tab, the taskbar thumbnail, and the title bar instead of the default WinUI 3 icon. Multi-resolution `Logo.ico` is bundled and applied via `AppWindow.SetIcon` plus `WM_SETICON` for the Alt+Tab thumbnail.

### 📚 Docs
- Added `docs/research/font-parsing.md` — comparison of `SixLabors.Fonts`, `HarfBuzzSharp`/`SkiaSharp`, `Typography`, `Win2D` + DirectWrite, and `SharpFont` for fixing WOFF2 metadata and cmap-aware glyph enumeration; includes a TTF file-association appendix.
- Added `docs/research/packaging-decision.md` — the unpackaged-vs-MSIX comparison applied feature-by-feature to Fontager, the HKCU virtualization and `.ttf` association deep-dives, what we lose / gain, and the recipe for switching back to MSIX when/if we list on the Microsoft Store later.

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
