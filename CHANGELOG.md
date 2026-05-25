# Changelog

## [1.3.0] - 2026-05-25

Minor release: dynamic dark/light preview backgrounds (contrast check) and integrated GitHub release update checking.

### ➕ Added
- **GitHub Release Update Checker**: Automatically queries the latest release from the GitHub API on startup (optimized to run at most once every 24 hours to avoid API rate limits).
- **Manual Update Checking in Settings**: Added a new "Updates" section in Settings allowing users to trigger manual checks on-demand, showing checking progression, and displaying the last checked timestamp in local time.
- **Title Bar Update Notification**: Integrates a prominent, high-visibility "Update Available" button (using accent styling) on the left side of the title bar, placed immediately after the action buttons (Open, Settings) for a clean, cohesive layout.
- **Default Preview Background Setting**: Added a configuration card in Settings -> Preview to let users choose their preferred default background contrast mode when opening fonts.
- **Quick View Background Setting**: Added a configuration card in Settings -> Quick View to select default contrast options specifically for the Quick View panel.

### 🔄 Changed
- **Architectural Refactoring (Viewer)**: Reorganized the Viewer project toward MVVM and shared Core services without changing the 1.3.0 feature set. Font installation, registry/file-association helpers, elevation relaunch, and Win32 file dialogs now live in `Fontager.Core` (`IFontInstallerService`, `FontInstallerService`, `ProcessElevationHelper`, `Win32FileDialog`, `FileAssociationService`). Settings use a dedicated `SettingsViewModel` with `{x:Bind}` two-way bindings. Glyph Unicode-block sidebar, category filters, and debounced search state moved into `FontViewerViewModel`. Font preview, glyphs, metadata, install actions, and empty/loading/error UI were extracted to `FontViewerPage`; `MainWindow` is now a navigation shell (custom title bar + `RootFrame`) with settings routed through the frame back stack.
- **Adaptive Waterfall & Character Grids**: Modified the waterfall layout and character preview grids to automatically adapt their text foreground and label colors when high-contrast modes are toggled.
- **Title Bar Passthrough Integration**: Registered the new title bar update button inside the interactive passthrough region tracker so click events are captured perfectly without interfering with window drag operations.
- **Streamlined Preview Toolbar**: Completely removed the redundant contrast paintbrush button from the size control stack panel in the main preview tab toolbar to minimize clutter.

### 🐛 Fixed
- **Settings ComboBox Rendering Bug**: Resolved a WinUI 3 empty-on-load rendering bug by deferring all ComboBox `SelectedIndex` index assignments to the next dispatcher tick (`DispatcherQueue.TryEnqueue`) within a transient state-lock, guaranteeing selections like *"System default"* appear immediately.
- **Dynamic Quick View Theme Restoration**: Solved the default Quick View background restoration bug by fetching and re-binding the native dynamic `{ThemeResource}` brushes (`CardBackgroundFillColorDefaultBrush` and `CardStrokeColorDefaultBrush`) directly from application resources.
- **Mixed Theme Contrast Propagation**: Resolved a WinUI 3 layout inheritance bug where a parent border's `RequestedTheme` fails to propagate down through scroll viewers, ensuring that size controls, textboxes, and labels remain fully visible when contrasting themes are mixed (e.g. system default is dark but preview background is light).

## [1.2.2] - 2026-05-21

Patch release: DPI-aware default window sizing and window layout persistence (size, coordinates, and maximized/windowed state).

### ➕ Added
- **DPI-Aware Default Sizing**: Defined the default initial window size in DIPs (`850x600`), which dynamically scales according to the active display's DPI scale factor (e.g. `1275x900` physical pixels on a 150% scaled 2K screen). This resolves the issue where the app's window appeared too large on `1366x768` (100% scaled) displays.
- **Window Layout & State Persistence**: The application now saves its window size, screen coordinates, and maximized/windowed state to `%LocalAppData%\Fontager\settings.json` on exit, restoring them perfectly on next launch.
- **Safe Off-Screen Layout Recovery**: Added a robust screen-intersection safety check using WinUI 3's `DisplayArea` API. If coordinates are found to be off-screen (e.g. if a secondary monitor was disconnected), the window safely centers on the primary display.
- **Adjustable Quick View Font Size**: Added a new slider under Settings -> Display to configure the character preview font size for the Quick View panel (ranging from `12px` to `48px`). The main window's Quick View panel now updates instantly in real-time when the setting is changed.

## [1.2.1] - 2026-05-20

Patch release: Restores standard Windows context menu right-click "Install" options when Fontager is the default handler, and introduces official links across the UI.

### ➕ Added
- **Standard Context Menu 'Install' Verbs Restored**: Registers the Windows `InstallFont` context menu shell extension under Fontager's custom ProgID (`Software\Classes\Fontager.Viewer.font\shellex\ContextMenuHandlers\InstallFont`), successfully restoring the localized "Install" and "Install for all users" options for `.ttf`, `.otf`, and `.ttc` files. Standard Windows right-click batch installation of multiple selected fonts is now fully supported.

### 🔄 Changed
- **Redesigned Welcome Screen Links**: Expanded the Welcome / Empty state screen to display a clean, structured set of official resource links: GitHub, Website, Changelog, and Roadmap.

## [1.2.0] - 2026-05-15

Minor release: optional always-on administrator mode for default font-file handling and system-wide installs.

### ➕ Added

- **Settings → UAC for all-users install** (recommended, on by default) — Fontager stays non-elevated while previewing fonts; Windows may show UAC **only** when you install to `C:\Windows\Fonts` for all users. Implemented via a short elevated helper process (`--install-all-users`).
- **Settings → Run entire app as administrator** — separate toggle (with restart dialog) for users who want the **whole** application elevated (e.g. always-on admin as the default font handler). Clearly distinguished from the all-users install option above.
- **`ProcessElevationHelper`** — elevation checks, full-app restart, and on-demand install elevation.
- **`FontInstallerService`** — machine-wide install logic shared by the main window and the elevated helper.
- **Startup** — if “Run entire app as administrator” is on and the process is not elevated, Fontager prompts for UAC before the main window (cancelled UAC continues non-elevated).

### 🔄 Changed

- Install target combo: **All users (Windows\Fonts)** is available when either UAC-for-install is enabled or the app is already elevated.
- Install section descriptions explain the difference between per-install UAC and full-app administrator mode.

## [1.1.2] - 2026-05-15

Patch release: reliable font preview after reinstall and across app restarts when installed under Program Files.

### ➕ Added

- **`FontCacheSetup`** — ensures a writable `FontCache` folder for WinUI `ms-appx:///FontCache/…` preview URIs when the install directory is read-only.
- **Inno Setup post-install** — creates a directory junction `{app}\FontCache` → `%ProgramData%\Fontager\FontCache` (removed on uninstall). Default install path remains `C:\Program Files\Fontager\Viewer\`.

### 🔄 Changed

- **Font preview cache** again resolves through the install-relative `FontCache` path (for `ms-appx`), not `%LocalAppData%` with `file:///`. Settings stay under `%LocalAppData%\Fontager\settings.json` via `FontagerPaths`.
- **Startup** — unpackaged builds call `FontCacheSetup.EnsureWritableCacheDirectory()` before opening fonts.

### 🐛 Fixed

- **System font shown instead of the opened font** on the second and later app launches (and intermittently on first launch) after 1.1.1’s `file:///` cache path — WinUI does not reliably load preview fonts via `file:///`.
- **Junction fallback** when the installer did not create the link: app attempts `mklink /J` or falls back to GDI private registration by family name.

## [1.1.1] - 2026-05-15

Installer-first distribution fix for Program Files installs: font preview cache and publish pipeline.

### ➕ Added

- **MIT `LICENSE`** at the repository root (referenced by README and CONTRIBUTING).
- **Inno Setup installer** — `installer/Fontager.Viewer.iss` builds `Fontager.Viewer-1.1.1-win-x64-setup.exe` (English/Turkish wizard, optional desktop shortcut, license step). Default install path: `C:\Program Files\Fontager\Viewer\`.
- **`FontagerPaths`** — shared `%LocalAppData%\Fontager` root for writable app data (settings + font preview cache).

### 🔄 Changed

- **Font preview cache** moved from the install directory to `%LocalAppData%\Fontager\FontCache`. Unpackaged builds load cached fonts via `file:///` so preview works when the app is installed under Program Files.
- **Publish pipeline:** MSBuild target copies WinUI `.xbf` and app `.pri` into the publish folder after `dotnet publish` / VS Publish (fixes empty UI when running from `win-x64\publish`).
- **`.gitignore`** — ignores Inno output (`installer/output/`), release zips, and `*-setup.exe`; tracks shared `*.pubxml` profiles, ignores `*.pubxml.user` only.

### 🐛 Fixed

- **“Access to the path …\FontCache is denied”** when opening font files after installing to Program Files and setting Fontager as the default handler.
- **Publish folder** missing compiled XAML (`.xbf`) and `Fontager Viewer.pri`, which prevented the app from starting when launched from `publish\` only.

## [1.1.0] - 2026-05-15

Unpackaged WinUI 3 distribution, a redesigned full-screen **Settings** experience, a rebuilt **font parsing and glyph** stack, WOFF2 end-to-end support, and stronger font installation management.

### ➕ Added

- **Font parsing and glyph system rebuilt end-to-end.** `FontParser` and the Glyphs UI were reworked together so coverage, metadata, and browsing stay in sync:
  - **`cmap`-aware coverage** — subtable formats 0, 4, 6, and 12; `FontMetadata.SupportedCodePoints` reflects what the font actually contains (CJK, icon fonts, symbol fonts no longer show an empty Latin-only grid).
  - **Unicode blocks and categories** — sidebar of blocks the font covers (with counts), chip filters (All, Uppercase, Lowercase, Numbers, Punctuation, Symbols, Accented, Other), and search by character, hex (`U+00A0` / `0x00A0` / `00A0`), or decimal code point.
  - **Glyph detail** — block and category on the detail card; copy glyph to clipboard; default to **Basic Latin** when present (`GlyphBlockEntry` and related helpers).
  - **WOFF2 on the same pipeline** — `Woff2Decoder` decompresses to SFNT; extended **name**, **OS/2**, and **head** metadata for a fuller property surface in the viewer.
- **`FontService`** WOFF2-aware metadata and collection counts; cache paths respect **packaged vs unpackaged** (`IsWindowsPackaged`); **`ms-appx` / relative** URIs for reliable preview loading.
- **Settings as a dedicated page (`SettingsPage`), not a dialog.** The old modal settings `ContentDialog` was removed. Settings now open as a full in-app page with a redesigned layout: grouped sections (appearance, fonts, file association, about), clearer navigation, and room for richer controls (backdrop, preview debouncing, install behavior, and more).
- **Installation hardening:** `RemoveFontResource` / `RemoveFontResourceEx`, **`InstallFontFileReplacingAsync`** (replace existing install with proper unload + refresh), **staging** for copies, and **`BroadcastFontChange`** so session and other apps pick up additions/removals consistently.
- **Post-install UX:** dedicated **success / warning / error** dialogs for font installation (instead of generic info dialogs).
- **Settings → after successful install:** optional **automatic exit** and a **brief success dialog** before quit (user-toggleable).
- **AssemblyTitle / Product** (and related) on **Viewer and Manager** project files for clearer build and shell identity.

### 🔄 Changed

- **Distribution switched to unpackaged WinUI 3** with the Windows App SDK runtime bundled self-contained. The MSIX manifest (`Package.appxmanifest`) stays in the repository so Store distribution can be re-enabled later as a single-property change. Rationale and tradeoffs are documented in `docs/research/packaging-decision.md`.
- Settings storage moved off `Windows.Storage.ApplicationData` and onto a plain JSON file at `%LocalAppData%\Fontager\settings.json`. The file is written atomically (temp + rename) so a mid-write power loss can't corrupt it. The new path is identical across packaging modes, so re-enabling MSIX later won't require a settings migration.
- `FileAssociationService` now registers the four supported font formats (`.ttf`, `.otf`, `.ttc`, `.woff2`) under one unified ProgID. **Settings →** file association exposes a single "Register Fontager for font files (current user)" toggle covering all four. Legacy single-extension entries from older installs are cleaned up automatically.
- **MainWindow layout** streamlined (title bar and chrome organization) for clearer structure and responsiveness.
- **Version sources:** version text uses package/manifest when available and **falls back to assembly informational version** so unpackaged and hybrid scenarios stay accurate (MainWindow and Settings).
- **FileAssociationService** packaging detection refactored to avoid spurious exceptions during version/probing checks.
- **Backdrop:** more backdrop options and **sync with persisted settings** across sessions (see Performance for backdrop apply optimizations).
- **Font installation UI** (Viewer and Manager): clearer command labels, **tooltips that reflect elevation** (per-user vs all-users), and settings copy that explains **when admin rights are required** for machine-wide installs.
- **Preview text in Settings** uses **debounced** updates with a tuned interval so the UI stays responsive while editing.
- **README** and **Fontager.Viewer.csproj** metadata updated for the unpackaged story: clearer install guidance (installer recommended), assembly **name/description** aligned with the product, and **font caching** paths/logic kept consistent with unpackaged layout.
- **FileAssociationService** handles **legacy application registrations** more aggressively so registry cleanup during (re)association is reliable.

### 🐛 Fixed

- **Settings → Fonts: uninstall fonts you installed with Fontager.** After installing a font (current user or all users), you can remove it from **Settings → Fonts** with a real **Uninstall** action — not just hide it from the list. Registry and session font resources are cleaned up as part of removal.
- "Install for current user" now actually shows up in Settings → Fonts without a logoff. After copying the font to `%LocalAppData%\Microsoft\Windows\Fonts` and writing `HKCU`, the app calls `AddFontResource` to register it in the current session and broadcasts `WM_FONTCHANGE` so the shell and the Font Cache service refresh immediately. The registry write is verified post-write and the user is told explicitly when the write is virtualized away by packaged identity.
- **Settings → Fonts uninstall** only became reliable after moving off MSIX: packaged runs virtualized our HKCU font entries, so Windows treated them as system-managed and Fontager could not truly remove them (only "Hide" in the UI). Unpackaged distribution plus correct registry value names (`" (TrueType)"` / `" (OpenType)"`) fixes both removal and Font Cache acceptance.
- Registry value names now use the `" (TrueType)"` / `" (OpenType)"` suffix Windows expects, so the Font Cache service stops silently rejecting the entry.
- Drag-and-drop and the "Open" file picker now work when Fontager is launched with "Run as administrator". `WM_DROPFILES`, `WM_COPYDATA`, and `WM_COPYGLOBALDATA` are whitelisted via `ChangeWindowMessageFilterEx` so lower-integrity Explorer can talk to the elevated window, and the WinRT `FileOpenPicker` (which fails under elevation) is replaced by a Win32 `IFileOpenDialog` in that case.
- Window now shows the Fontager logo in Alt+Tab, the taskbar thumbnail, and the title bar instead of the default WinUI 3 icon. Multi-resolution `Logo.ico` is bundled and applied via `AppWindow.SetIcon` plus `WM_SETICON` for the Alt+Tab thumbnail.
- **WOFF2 installation restrictions** communicated in-product where applicable so users aren’t left without context when an action isn’t supported for that format.

### ⚡️ Performance

- Glyphs tab no longer freezes on CJK or emoji fonts with 10k+ glyphs. The root cause was UI virtualization being disabled — the `GridView` was nested inside a `ScrollViewer > StackPanel`, which handed it infinite height and forced every cell to materialize up-front. The GridView now owns its own scrolling and `ItemsWrapGrid` virtualizes off-screen rows.
- `GlyphItem` now precomputes its `Block` and `Category` once at construction so per-keystroke filtering no longer calls `GlyphCategoryClassifier.Classify(...)` thousands of times.
- Glyph search input is debounced to 150 ms so a fast typist doesn't rebuild the filtered grid on every keystroke.
- `FontFamily` is set once on the `GridView` (and inherits to per-cell `TextBlock`s) instead of being assigned per realized container as the user scrolls.
- `FontViewerViewModel.GlyphItems` is now a plain `List<T>` rather than `ObservableCollection<T>`: it's only ever read in bulk by code-driven filtering, so the change-notification machinery was pure overhead on every font load.
- **Window backdrop no longer recreated on every settings save.** `SystemBackdrop` was removed from static XAML and is applied only from `ApplyBackdrop()`. `_appliedBackdropKind` remembers the last mode (Mica, Acrylic, Mica Alt, or solid); when the user saves settings without changing the backdrop, we skip allocating a new `MicaBackdrop` / `DesktopAcrylicBackdrop` instance. Previously each save replaced the backdrop and caused visible Mica/Acrylic flashes plus unnecessary compositor work.

### 📚 Docs

- Added `docs/research/font-parsing.md` — comparison of `SixLabors.Fonts`, `HarfBuzzSharp`/`SkiaSharp`, `Typography`, `Win2D` + DirectWrite, and `SharpFont` for fixing WOFF2 metadata and cmap-aware glyph enumeration; includes a TTF file-association appendix.
- Added `docs/research/packaging-decision.md` — the unpackaged-vs-MSIX comparison applied feature-by-feature to Fontager, the HKCU virtualization and `.ttf` association deep-dives, what we lose / gain, and the recipe for switching back to MSIX when/if we list on the Microsoft Store later.
- **Revised research docs for 1.1.0 clarity:** `font-metadata.md`, `font-parsing.md`, `font-properties.md` (index-style property mapping), and `packaging-decision.md` refreshed to match the unpackaged Viewer and current parsing pipeline.

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

