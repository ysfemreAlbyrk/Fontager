# Font Parsing & Rendering - Research Report

## 1. What we have today

Fontager currently uses a custom, allocation-light binary parser at
[`Fontager.Core/Helpers/FontParser.cs`](../../Fontager.Core/Helpers/FontParser.cs).
Strengths and gaps as of this report:

| Area | State |
|---|---|
| TTF/OTF detection | Yes — magic-number + extension heuristic. |
| TTC parsing | Yes — `ttcf` header, per-font offset table, `numFonts`. |
| `name` table | Yes — Windows-Unicode (3,1), Mac Roman (1,0), Unicode (0). English (0x0409 / 0) is preferred. |
| `OS/2` table | Yes — `usWeightClass`, `fsSelection` (italic/oblique), PANOSE family byte, `achVendID`. |
| `head` table | Yes — `unitsPerEm`. |
| `maxp` table | Yes — `numGlyphs`. |
| `fvar` detection | **Detected only** — `IsVariable` is set; axes, instances, defaults are not parsed. |
| `cmap` table | **Not parsed** — this is the root cause of the "glyph rendering issues" the user reported. The Glyphs tab hard-codes Basic Latin + Latin-1 Supplement + Latin Extended-A regardless of what the font actually contains. |
| WOFF / WOFF2 | **Not parsed** — `FontService.LoadFontAsync` short-circuits to a filename-based heuristic (`CreateMetadataFromFileName`) for `.woff2`. Metadata is essentially fake. |
| Hinting / shaping | Not applicable — rendering is delegated to DirectWrite via XAML `FontFamily` and `ms-appdata:///…` URI caching. |
| GSUB / GPOS / OpenType features | Not parsed. Variable axes, stylistic sets, alternate glyphs are inaccessible to the UI. |

> **Failure modes observed by the user**
>
> - "Glyph rendering issues" — likely (a) the Glyphs grid only iterates Latin
>   ranges, so CJK / symbol / icon fonts look almost empty, and (b) WOFF2 falls
>   back to a filename-derived family name, which causes WinUI's font
>   resolver to render with the system fallback instead of the actual face.
> - "Certain file formats not displaying correctly" — WOFF2 in particular.
>   WOFF2 bodies are Brotli-compressed SFNT containers; until they're
>   decompressed we have no real metadata for them.

## 2. Library candidates

All packages below are available on NuGet and target either `net8.0` or
`netstandard2.0+`, which means each is consumable from
`Fontager.Core` (`netstandard2.0`-style class library) and
`Fontager.Viewer` (WinUI 3 / `net8.0-windows10.0.19041.0`).

### 2.1 `SixLabors.Fonts`

- **Type:** 100% managed C# (no native binaries).
- **License:** Apache 2.0 (FOSS-friendly, MIT-compatible attribution-only).
- **Repo:** [SixLabors/Fonts](https://github.com/SixLabors/Fonts).
- **Format support:** TTF, OTF, TTC (collections), WOFF, WOFF2 (Brotli built-in via `System.IO.Compression.Brotli` on .NET 6+).
- **What it gives us:**
  - `FontDescription` and `FontFamily`/`Font` types with rich metadata: family, sub-family, designer, copyright, description, license URL.
  - Full `cmap` lookup — `Font.TryGetGlyphMetrics(int codePoint, …)` and `FontFamily.HasCodePoint` work for any code point.
  - Variable font axes via `FontFamily.GetAvailableStyles()` + `FontVariation`.
  - OpenType features list (GSUB feature tags).
- **What it does not give us:**
  - It's a metrics/parsing library, not a rasterizer. You still need DirectWrite, Win2D, Skia, or System.Drawing to actually paint glyphs.
  - Some less common cmap subtables (e.g., subtable 13) are missing — not a blocker for desktop fonts.
- **Size impact (rough):** ~1.2 MB managed DLL, no native dependencies. Trim-friendly.
- **Verdict:** Strongest pure-managed candidate for *parsing*. Cleanly replaces the entire body of our custom `FontParser` with idiomatic, well-tested code, and crucially fixes both WOFF2 and `cmap` gaps in one move.

### 2.2 `HarfBuzzSharp` (usually paired with `SkiaSharp`)

- **Type:** Managed wrapper over native HarfBuzz binaries.
- **License:** MIT (HarfBuzz itself is MIT).
- **Repo:** [mono/SkiaSharp](https://github.com/mono/SkiaSharp).
- **Format support:** Anything HarfBuzz reads (TTF, OTF, TTC, variable fonts). WOFF2 needs an external decoder — HarfBuzz expects an already-decompressed SFNT blob.
- **What it gives us:**
  - Industry-standard text shaping. Complex scripts (Arabic, Devanagari, CJK) render with correct ligatures and joining behaviour.
  - Per-glyph metrics, OpenType feature toggles, variable-axis support.
- **What it does not give us:**
  - Just a shaping engine. We'd pair it with SkiaSharp to actually rasterize, which more than doubles the install size.
  - Doesn't help with metadata extraction — there's no first-class "give me the name table" API; you'd still need parsing on top.
- **Size impact:** Significant. SkiaSharp's native blobs for `win-x64` / `win-arm64` are ~6–8 MB each *per RID*, and ARM64 is required for the project. Probably an extra **15–25 MB** in the MSIX once both RIDs are included.
- **Verdict:** Overkill for "I want to know what glyphs this font has". Only worth it if Fontager evolves into a true font workshop that needs accurate shaping across scripts. Park for the Manager project, not the Viewer.

### 2.3 `Typography` (LayoutFarm)

- **Type:** Pure managed C#.
- **License:** MIT.
- **Repo:** [LayoutFarm/Typography](https://github.com/LayoutFarm/Typography).
- **Format support:** Very wide — TTF, OTF, TTC, WOFF, WOFF2, plus a managed Brotli decoder.
- **What it gives us:**
  - Full table parser, cmap reader, glyph-outline reader, variable axis support, GSUB/GPOS, even a managed glyph renderer.
  - Has stand-alone modules for each concern, which is nice if we only want one.
- **What it does not give us:**
  - Maintenance is sporadic — the last release on NuGet is older and the repo is less active than SixLabors.
  - API surface is sprawling and not particularly idiomatic for .NET 8 (lots of `public` fields, mutable structs, inconsistent naming).
  - Trimming friendliness and AOT readiness are unknown.
- **Size impact:** Comparable to SixLabors.Fonts, all managed.
- **Verdict:** Capable but feels like a long-term liability. Would only pick it if SixLabors.Fonts proved insufficient for some specific table.

### 2.4 `Microsoft.Graphics.Canvas` (Win2D) + DirectWrite P/Invoke

- **Type:** First-party Microsoft component. Win2D is a managed wrapper over Direct2D / DirectWrite.
- **License:** MIT (Win2D); DirectWrite ships with Windows.
- **Repo:** [microsoft/Win2D](https://github.com/microsoft/Win2D).
- **Format support:** Whatever DirectWrite supports — TTF, OTF, TTC, WOFF, WOFF2 are all fine on Windows 10 1809+.
- **What it gives us:**
  - `CanvasFontSet.GetCustomFontSet` lets us enumerate a single font file's faces and read every standard metadata property without writing a parser.
  - Real `IDWriteFontFace3` for shaping, glyph indices, cmap-equivalent (`GetGlyphIndices`).
  - Native rendering quality, zero extra distribution weight (DirectWrite ships with Windows).
- **What it does not give us:**
  - The API is COM-heavy, and Win2D's coverage of `IDWriteFontSetBuilder`, `IDWriteFontResource`, `IDWriteFactory7` features is partial as of Win2D 1.4.x. You'll fall through to raw `CsWin32`/`Microsoft.Windows.CsWin32` P/Invoke for non-trivial use cases.
  - Adds a Win2D dependency (a few MB managed + a small native helper). Acceptable for a Windows-only product.
- **Size impact:** Modest. Win2D `Microsoft.Graphics.Canvas.WinUI` is roughly 3–5 MB across all RIDs.
- **Verdict:** Best long-term answer for *Windows-only* projects that want to ship as little as possible while still getting first-class shaping/rendering. Steepest learning curve.

### 2.5 `SharpFont` (FreeType bindings)

- **Type:** Managed bindings over native FreeType.
- **License:** MIT bindings, but FreeType itself is dual-licensed FTL/GPLv2. The FTL ("FreeType License") is compatible with MIT in practice but requires a credit line in product docs. Not a problem for our FOSS app but worth a note.
- **Format support:** Everything FreeType supports — TTF, OTF, TTC, WOFF (WOFF2 requires the optional `woff2` module compiled in).
- **What it gives us:**
  - High-fidelity rasterization with hinting.
  - Familiar API to many designers/dev tools.
- **What it does not give us:**
  - Idiomatic .NET 8. The bindings haven't kept pace with .NET evolution.
  - Native binaries to ship for `win-x64` and `win-arm64`; the WOFF2 module is non-trivial to build.
- **Verdict:** Skip. No advantage over the other choices in a Windows-only WinUI 3 app, and the native build/distribution story is uglier than HarfBuzz/Skia.

## 3. Comparison matrix

> "What we need" axes are weighted toward Fontager's immediate problems
> (cmap, WOFF2, low binary-size budget).

| Capability / concern | Custom `FontParser` (today) | SixLabors.Fonts | HarfBuzz + Skia | Typography | Win2D + DirectWrite | SharpFont |
|---|---|---|---|---|---|---|
| Native deps | None | None | Yes (large) | None | Tiny (system DWrite) | Yes |
| WOFF2 | No | **Yes** | No (needs external) | Yes | **Yes** (system) | Optional module |
| Full `cmap` enumeration | No | **Yes** | Yes | Yes | **Yes** | Yes |
| Variable axes (read) | Flag only | Yes | Yes | Yes | Yes | Yes |
| OpenType features | No | Partial | **Yes (best)** | Yes | Yes | Yes |
| Glyph shaping (Arabic, CJK) | n/a | Lookup only | **Yes** | Limited | **Yes** | Yes |
| Glyph rasterization | n/a | No | **Yes** | Yes | **Yes** | **Yes** |
| .NET 8 idiomatic | Yes (it's ours) | Yes | Yes | Mixed | Yes | Outdated |
| Maintenance level | Us | Active | Active | Sporadic | Active (MS) | Quiet |
| License fit (FOSS) | n/a | Apache 2.0 | MIT | MIT | MIT | MIT + FTL |
| MSIX size impact (x64 + ARM64) | 0 | ~1.2 MB | **+15–25 MB** | ~1.5 MB | ~3–5 MB | ~5–8 MB |
| Effort to integrate (rough) | n/a | **Low** | Medium-high | Medium | High | High |

## 4. Concrete failure cases we'd recover by switching

1. **Material Icons / Noto Symbols / icon fonts** — the Glyphs tab today shows
   the wrong characters because we iterate `U+0020–U+017F`. With a proper
   `cmap` parser (any of the libraries, or even a small extension to our
   own) we'd render exactly the supported glyphs.
2. **WOFF2 metadata** — fonts like `Inter-Bold.woff2` currently render under
   the *filename* as their family, which means DirectWrite picks the system
   default instead of the real face for the preview text. SixLabors.Fonts
   or Win2D both fix this in one call.
3. **CJK / Cyrillic / Arabic preview** — same root cause as #1. The font
   technically loads (XAML renders it) but the user can't browse the
   characters because the Glyphs grid never enumerates them.
4. **Variable font UX (future)** — needed once we add the axis sliders
   from the PRD. SixLabors.Fonts and DirectWrite both expose
   axes/instances; we'd otherwise need to extend the custom parser to
   read `fvar` / `STAT` / `avar`.

## 5. Recommendation

**Keep the custom parser for now; add `SixLabors.Fonts` as the
"heavy lifter" for cmap + WOFF2 + variable-axis introspection.**

Rationale:

- The two pain points the user is feeling today — "WOFF2 doesn't display
  correctly" and "Glyphs screen is wrong for non-Latin fonts" — are both
  solved by `cmap` enumeration and WOFF2 decompression. SixLabors.Fonts
  delivers both with zero native dependencies and a ~1.2 MB managed footprint.
- Keeping our existing `FontParser` initially lets us migrate
  incrementally: ship the cmap fix on top of SixLabors.Fonts while leaving
  the rest of the codepath untouched.
- DirectWrite/Win2D remains the long-term destination if (and only if)
  Fontager grows into a real shaping/rendering tool. We don't need that
  for the Viewer's MVP and don't want to pay the COM/native cost yet.
- We explicitly reject HarfBuzz/Skia for now because of the binary-size
  budget (PRD: "<20 MB installed") — the native blobs alone would
  exceed that.

### Suggested follow-up path (not part of this report)

1. Add `SixLabors.Fonts` as a `Fontager.Core` dependency.
2. Introduce `IFontMetadataReader` so the implementation is swappable.
3. Move WOFF2 + cmap reads behind the new reader.
4. Park rendering on DirectWrite / Win2D for a later "Fontager.Manager"
   milestone where shaping correctness starts to matter.

## 6. TTF file-association limitation on Windows (appendix)

This is closely related to the parser question but the constraint is
deployment, not parsing. Documented here so the research lives in one
place.

### Why MSIX cannot register `.ttf`

The MSIX schema's
[`windows.fileTypeAssociation`](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap-filetypeassociation)
extension treats `.ttf` as a *reserved* extension because the OS-bundled
"Windows Font Viewer" claims it via the
`Windows.Devices.Fonts` PROGID. Submissions that include `.ttf` in
`<uap:SupportedFileTypes>` are rejected at packaging time. This is why
[`Fontager.Viewer/Package.appxmanifest`](../../Fontager.Viewer/Package.appxmanifest)
only lists `.otf`, `.ttc`, and `.woff2`.

### Workarounds, in order of cost

1. **"Open with → Always" (manual)** — works today, no code change.
   Documented in the README. Limitation: users have to do it once per
   file type.
2. **HKCU `OpenWithProgids` (per-user, code change)** — for the
   *unpackaged / portable* build, write under:

   ```text
   HKCU\Software\Classes\.ttf\OpenWithProgids\Fontager.Viewer
   HKCU\Software\Classes\Applications\Fontager.Viewer.exe\shell\open\command
   ```

   This adds Fontager to the "Open with…" list for `.ttf`. We never
   claim default — Windows still owns it — but the entry is one click
   away. Under MSIX identity this write is virtualized into the package
   container, so it's effectively a no-op there. We can detect MSIX
   identity at runtime and only offer the button when running unpackaged.

3. **Out-of-process portable launcher** — ship a tiny unpackaged
   stub executable alongside the MSIX that owns `.ttf`. Overkill for
   our use case, listed for completeness.

The plan item that follows this report (item 5) implements option 2.

## 7. Decision log

- *2026-05-13* — Report drafted. Pending owner decision: adopt
  `SixLabors.Fonts` in a follow-up commit, or hold and ship targeted
  fixes (cmap parser extension, opt-in TTF registration) directly on
  the custom parser first. The plan currently follows the second
  approach to keep each commit small.
