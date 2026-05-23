# Roadmap

High-level picture of what Fontager has shipped and what is planned next. Release details and dates are in [CHANGELOG.md](CHANGELOG.md).

**Current focus:** Fontager.Viewer polish and reliability. Fontager.Manager is a longer-term track.

## General Project Goals

High-level vision and cross-component milestones for Fontager.

- [ ] **Windows Store & Packaged (MSIX) Distribution** — Bring Fontager to the official Microsoft Store to maximize reach and installation ease.
  - *Dependency:* Requires full implementation of **Fontager.Manager**'s custom library and font uninstallation UI to bypass the native Windows Settings "Uninstall" gap caused by registry virtualization in sandboxed packaged apps.
  - *Manifesting:* Limit packaged file-type associations to `.otf`, `.ttc`, and `.woff2` due to system reservation constraints on `.ttf`.
- [ ] **Unified Engine Consolidation** — Migrate all font installation, file association, and preview rendering primitives into the shared `Fontager.Core` library. Both the lightweight `Viewer` and the full `Manager` will run on the identical, high-performance C# parsing and rendering core.

---

## Fontager.Core

Shared models, parsing, and font services used by Viewer and Manager.

**Direction:** Anything both Viewer and Manager need — especially **font rendering** and **glyph rendering** — moves here so UI projects stay thin (WinUI chrome, navigation, app-specific workflows only).

- [x] **Custom binary `FontParser`** — TTF/OTF/TTC metadata without external font libraries
- [x] **`cmap` table support** — formats 0, 4, 6, 12; real Unicode coverage via `SupportedCodePoints`
- [x] **`Woff2Decoder`** — WOFF2 → SFNT for parsing and preview (decode only; encode/conversion still planned)
- [x] **Extended metadata** — additional name IDs; **OS/2** and **head** fields on `FontMetadata`
- [x] **`FontService`** — format detection, loading, WOFF2-aware collection counts
- [x] **Cache paths** — packaged vs unpackaged (`IsWindowsPackaged`); `ms-appx` / `ms-appdata` URI handling
- [x] **Glyph classification** — Unicode blocks and categories (`UnicodeBlocks`, `GlyphCategory`, classifiers)
- [ ] **Broader table coverage** — optional deeper tables where the viewer needs them (e.g. GSUB/GPOS awareness for display-only features)
- [ ] **Parser hardening** — clearer errors and recovery for damaged or exotic SFNT inputs
- [ ] **Shared logging hooks** — diagnostics surfaced to Viewer (and later Manager) without duplicating logic
- [ ] **Font conversion pipeline** — shared API to read SFNT, transform, and write output (used by Manager; optional Viewer “Export as…” later)
- [ ] **WOFF2 encode** — SFNT → WOFF2 (complement to existing `Woff2Decoder`)
- [ ] **Format export** — TTF ↔ OTF/CFF outlines where applicable; single-face extract from `.ttc`
- [ ] **Subsetting** — Unicode range / glyph-list subsetting while preserving required tables (`cmap`, `name`, metrics)
- [ ] **Conversion options** — hinting strip/preserve, table whitelist, metadata rewrite hooks
- [ ] **Batch conversion** — queue many files with consistent options (Core service; Manager UI)
- [ ] **Font rendering (Core)** — shared preview/sample text rendering (editable preview, waterfall, compare panes); Viewer and Manager call the same API instead of duplicating draw paths
- [ ] **Glyph rendering (Core)** — shared glyph cell, detail, and metrics/outline drawing for grids and inspectors
- [ ] **Rendering backend abstraction** — single interface over the chosen stack (e.g. DirectWrite / Win2D / future option) so neither app owns low-level font draw code
- [ ] **Move installation primitives to Core** — copy to fonts folder, registry (`HKCU`/`HKLM`), `AddFontResource` / `RemoveFontResource`, `WM_FONTCHANGE`; apps keep menus and dialogs only
- [ ] **Move glyph browse model to Core** — block sidebar, category chips, search/debounce inputs, filtered glyph lists (UI binds; logic not forked per app)
- [ ] **Consolidate font load + cache** — one path from file/URI → loaded family + temp cache used by preview and Manager library thumbnails
- [ ] **Shared settings schema** — keys, defaults, and validation in Core; Viewer/Manager only choose storage location and UI
- [ ] **Shared file-association helpers** — ProgID registration and cleanup APIs in Core (Settings toggles stay in each app); includes registering standard shell verbs (e.g. "Install") to restore context menus under our custom ProgID
- [ ] **Shared diagnostics** — logging, error codes, and parser/install failure messages defined once in Core

---

## Testing

- [ ] **`Fontager.Core.Tests`** — xUnit (or similar) project referencing Core only; runnable with `dotnet test`
- [ ] **Parser fixtures** — small committed fonts + golden expectations for `FontParser` (name, OS/2, head, `cmap` code points)
- [ ] **`Woff2Decoder` tests** — decompress known WOFF2 samples; compare checksum / table layout to reference SFNT
- [ ] **Glyph classifier tests** — block/category mapping for representative code points

---

## Fontager.Viewer

Lightweight font previewer; default handler for font files on Windows.

- [x] **Instant preview** — `.ttf`, `.otf`, `.ttc`, `.woff2`
- [x] **Editable preview text** and adjustable size
- [x] **Quick View** — character set overview in the header
- [x] **Waterfall view** — multiple sizes at once
- [x] **Glyph grid** — Unicode blocks, category filters, hex/decimal search, detail card
- [x] **Copy glyph to clipboard** — from the glyph detail UI
- [x] **Font metadata panel** — family, designer, license, version, and extended fields
- [x] **Multi-font navigation** — fonts inside `.ttc` collections
- [x] **Drag & drop** and file activation (double-click / open with)
- [x] **Font installation** — current user or all users; session refresh (`AddFontResource`, `WM_FONTCHANGE`)
- [x] **Settings → Fonts uninstall** — remove fonts installed via Fontager (unpackaged path)
- [x] **Settings page** — full in-app `SettingsPage` (replaces modal dialog); JSON settings at `%LocalAppData%\Fontager\settings.json`
- [x] **File association** — unified registration for `.ttf`, `.otf`, `.ttc`, `.woff2` (current user)
- [x] **Backdrop options** — Mica, Acrylic, Mica Alt, solid; persisted and applied without unnecessary recreation
- [x] **Elevation** — install targets and tooltips reflect admin vs standard user; drag-drop and open dialog work when run as administrator
- [x] **Glyph performance** — virtualized grid, debounced search/filter, precomputed block/category on `GlyphItem`
- [ ] **Dark / light preview backgrounds** — toggle preview surface for contrast checks
- [ ] **Recent files** — quick reopen list on the empty state
- [ ] **Update checks via GitHub Releases** — check for new versions on startup(only once a day) or on-demand in settings and notify the user when a new update is available.
- [x] **Restore context menu "Install" options** — prevent standard Windows right-click "Install" and "Install for all users" from disappearing when Fontager is the default handler
- [x] **Batch font installation** — support installing multiple selected fonts at once from Explorer
- [ ] **Internationalization (i18n)** — localized UI strings
- [ ] **Logging** — structured, opt-in diagnostics (install, load, parser failures)
- [ ] **Adopt Core font & glyph rendering** — drop Viewer-local render helpers once Core preview/glyph APIs ship
- [ ] **Adopt Core install + glyph browse APIs** — thin wrappers over Core for install UI and Glyphs tab
- [ ] **Smoke test checklist in CI (optional)** — scripted launch / headless checks only after Core tests exist; Viewer remains mostly manual until UI automation is justified
- [ ] **Windows Store & MSIX Distribution** — Transition Fontager.Viewer to a packaged MSIX app for Windows Store distribution. **Prerequisite: Fontager.Manager implementation.** This requires addressing key platform hurdles:
  - *HKCU Registry Virtualization Gap*: MSIX redirects `HKCU` writes to a private virtual registry hive. Because per-user font installations register in the sandboxed hive rather than the real `HKCU`, Windows Settings -> Fonts only offers to "Hide" rather than "Uninstall" them. **To resolve this, we must first implement Fontager.Manager** so users can manage and uninstall installed fonts directly through Fontager's UI, bypassing the native Windows Settings limitation.
  - *The `.ttf` Manifest Restriction*: The Windows Appx Manifest schema strictly blocks packaging declarations of the `.ttf` extension as it is reserved for the system font viewer. Packaging requires limiting manifest-declared file-type associations to `.otf`, `.ttc`, and `.woff2`, and dynamically disabling `.ttf` default handling settings in packaged mode.
  - *Elevation & Sandboxing*: Ensure the `runFullTrust` capability is declared in `Package.appxmanifest` to support UAC-prompted "Install for all users" (which writes to `HKLM` and `C:\Windows\Fonts`) from the packaged sandbox.
  - *Path Portability & Settings*: Adapt path resolution so the existing JSON storage schema at `%LocalAppData%\Fontager\settings.json` is mapped cleanly, or seamlessly bridge to WinRT AppData storage under packaged execution.

---

## Fontager.Manager

Professional font management suite — **planned**, not in active development.
- [x] **Project scaffold** — shared Core reference; assembly metadata aligned with Viewer
- [ ] **Library** — organize installed and project fonts; collections and tagging
- [ ] **Activation** — temporary font activation without permanent install
- [ ] **Discovery** — Google Fonts integration
- [ ] **Compare & export** — side-by-side comparison; export font catalogs
- [ ] **Convert fonts (UI)** — pick source format(s), target format, and output folder; progress for batch jobs
- [ ] **WOFF2 for web** — export desktop fonts (TTF/OTF) to WOFF2 for web projects
- [ ] **Desktop formats from WOFF2** — WOFF2 → TTF/OTF for local install or editing
- [ ] **TTC tools** — split collection into single fonts or merge faces into a `.ttc` (where technically feasible)
- [ ] **Subset for delivery** — export a smaller font file by script, language, or custom glyph list (licensing-aware workflow)
- [ ] **Convert from library** — right-click fonts in the library: convert, subset, or re-export without leaving Manager
- [ ] **Adopt Core font & glyph rendering** — library thumbnails, compare view, and glyph inspectors use the same Core draw stack as Viewer
- [ ] **Adopt Core install + conversion APIs** — batch install/activate and convert jobs orchestrate Core services, not duplicated Manager logic

---

## How to suggest changes

Open a [GitHub issue](https://github.com/ysfemreAlbyrk/Fontager/issues) or discuss in a pull request. Items move from **Planned** to **Completed** when they ship in a tagged release (see CHANGELOG).
