# Font parsing & rendering

I write these notes mostly for Future Me: what Fontager actually does with binary fonts, why I didn’t just pull in the biggest NuGet library and call it a day, and where the sharp edges still are. The deeper table-by-table reference lives in [`font-metadata.md`](./font-metadata.md); this file is the narrative version.

---

## 1. What ships in the product today

The pipeline is intentionally boring: **one load path** in [`FontService`](../../Fontager.Core/Services/FontService.cs). If the file is WOFF2, we decode to raw SFNT bytes first ([`Woff2Decoder`](../../Fontager.Core/Helpers/Woff2Decoder.cs)); otherwise we read the file as-is. Then [`FontParser`](../../Fontager.Core/Helpers/FontParser.cs) walks the SFNT directory (including TTC offsets) and fills [`FontMetadata`](../../Fontager.Core/Models/FontMetadata.cs). The viewer binds that model and asks DirectWrite / XAML to render — we’re not rasterizing outlines ourselves.

| Area | State |
|---|---|
| TTF / OTF detection | Yes — magic + extension. |
| TTC | Yes — `ttcf`, per-face offset, navigation in the viewer. |
| WOFF2 | **Yes** — Brotli decompress + glyf/loca reconstruction where the spec requires it; then the same parser as desktop fonts. Rare transforms we skip are noted in `font-metadata.md`. |
| `name`, `head`, `OS/2`, `hhea`, `post`, `maxp` | Yes — drives the Info tab and family naming. |
| `cmap` | **Yes** — we pick a sensible subtable, support common formats (incl. format 12 for non-BMP), and expose supported code points to the Glyphs tab. No more “pretend everything is Latin”. |
| `fvar` | Axes read (tag, name, min/default/max). Named instances — not surfaced in the UI yet. |
| GSUB / GPOS | We **list feature tags** the tables advertise; we don’t interpret lookups or simulate shaping. |
| Hinting / shaping | Not our job — rendering goes through the system stack. |
| Failure fallback | If parsing leaves us without a usable family name (bad file or decoder gap), we fall back to filename heuristics so the UI doesn’t go blank. |

So the old pain points — “WOFF2 looks like a random filename font” and “icon fonts show an empty grid” — were symptoms of **not** having SFNT bytes and **not** reading `cmap`. Those paths exist now. What we still *don’t* do is HarfBuzz-grade shaping or draw Béziers by hand; that’s a different product.

---

## 2. Why I still like the custom stack

Pulling SixLabors.Fonts or similar would delete a lot of our code and probably stay maintained longer than my Saturday afternoons. I stick with the hand-rolled parser for a few pragmatic reasons:

- **No extra dependency surface** for something we already understand — especially WOFF2, where we only needed decode-to-SFNT plus metadata, not a full layout engine.
- **Allocation and control** — we only parse what the viewer needs, on a background thread, with fallbacks we own.
- **Windows-first** — ultimate glyph drawing is always DirectWrite anyway; the parser’s job is truthful metadata and coverage for our UI.

That said, I’m not religious about it. If we ever need bulletproof layout tables or subsetting for export, revisiting the library comparison below is fair game.

---

## 3. Library notes (from when I compared options)

These are still useful as a cheat sheet. All are viable technically; the question was fit for *this* app’s scope and size budget.

### `SixLabors.Fonts`

Managed C#, Apache 2.0. Strong cmap + WOFF2 story, good metadata. Not a rasterizer — we’d still use DirectWrite for pixels. Would replace most of `FontParser` in one dependency.

### `HarfBuzzSharp` (+ Skia)

The serious shaping path. Native binaries, bigger footprint, overkill for “show me what glyphs exist” unless we lean into complex-script fidelity in-app.

### `Typography` (LayoutFarm)

Very capable pure C#, MIT. Broader than we need; maintenance and API ergonomics worried me more than the code itself.

### Win2D + DirectWrite

First-party, smallest *extra* weight if we leaned entirely on DWrite for enumeration. COM-heavy; great when we’re okay being Windows-only forever (we are, for the viewer).

### `SharpFont` (FreeType)

Skipped — native FreeType lifecycle for a WinUI app that already has DirectWrite didn’t buy enough.

### Comparison matrix

Weighted toward: cmap + WOFF2 + small footprint + “we’re not shipping a paint stack”.

| Capability / concern | Custom stack (today) | SixLabors.Fonts | HarfBuzz + Skia | Typography | Win2D + DWrite | SharpFont |
|---|---|---|---|---|---|---|
| Native deps | None | None | Yes (large) | None | Tiny | Yes |
| WOFF2 → usable SFNT | Yes (ours) | Yes | Needs decode first | Yes | Yes (OS) | Optional |
| Full cmap enumeration | Yes | Yes | Yes | Yes | Yes | Yes |
| Variable axes (read) | Axes yes; UI sliders later | Yes | Yes | Yes | Yes | Yes |
| OpenType layout simulation | Tags only | Partial | **Strong** | Yes | Yes | Yes |
| Rasterization | DWrite / XAML | External | Skia | Optional | Win2D | FreeType |
| Maintenance | Us | Active | Active | Sporadic | Microsoft | Quiet |

---

## 4. Problems we actually solved vs. what’s left

**Solved (for normal desktop / web fonts):**

- Icon fonts and symbol fonts appearing “empty” in Glyphs — fixed once `cmap` drove the grid.
- WOFF2 previews resolving to the wrong face — fixed once decode → SFNT → real `name` table.

**Still rough edges:**

- **Exotic WOFF2** — encoder-only transforms we don’t implement yet fall through to filename fallback (called out in `font-metadata.md`).
- **Variable instances** — axes show in Info; picking “Bold widened” presets in the UI is still todo.
- **Shaping** — Arabic joins and Indic clusters won’t match Notepad’s glyph-by-glyph progression in our grid; we’re showing encoded coverage, not text runs through a shaper.

---

## 5. Why MSIX can’t own `.ttf` registrations

This overlaps heavily with [`packaging-decision.md`](./packaging-decision.md), but it belongs here because people grep “font parsing” and wonder why `.ttf` is special.

The MSIX manifest schema treats `.ttf` as a **reserved** extension — the OS font viewer path wins at validation time. Our packaged manifest intentionally lists `.otf`, `.ttc`, `.woff2` only ([`Package.appxmanifest`](../../Fontager.Viewer/Package.appxmanifest)).

**Workarounds:**

1. User picks “Open with → Always” manually — works but friction.
2. **Unpackaged build:** we write HKCU `OpenWithProgids` so Fontager appears under “Open with…”. Real hive, not virtualised.
3. **Packaged build:** that HKCU write disappears into the package container — we detect packaged mode and disable the toggle rather than lie to the user.

---

## 6. Decision log

- **2026-05-13** — First draft of this doc; parser was thinner; noted cmap/WOFF2 gaps.
- **2026-05 (later)** — Shipped WOFF2 decode in-process, real `cmap` enumeration, richer `name` / `OS/2` / `head` / `post` / `hhea`, `fvar` axes, GSUB/GPOS tag harvesting. Updated this file so it matches reality instead of the wishlist.

If you’re reading an old branch and the behaviour doesn’t match the table in §1, trust the code paths linked above.
