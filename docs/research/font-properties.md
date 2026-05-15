# Font properties

This is the **human-facing index**: each field we show (or might show) on the Info tab, where it came from in the binary, and why a regular user might care — line metrics arguments, “why does my icon font family look wrong?”, that sort of thing.

[`font-metadata.md`](./font-metadata.md) is the companion organised by **tables and offsets**; open that when you’re hex-diving. Open **this** file when you’re staring at `Fontager.Core.Models.FontMetadata` and wondering whether a property is cosmetic or load-bearing.

---

## 1. Identification

These properties answer "what is this font called and who made it?".

| Property | Source | Notes |
|---|---|---|
| `FamilyName` | `name` ID 1 | The basic family name, e.g. `Inter`. Used by older Windows and most CSS pickers. |
| `TypographicFamilyName` | `name` ID 16 | The "preferred" family name. Many large families (Material Icons, Noto Sans CJK) live entirely under name ID 16 — DirectWrite and modern CSS resolve via this when present. Fontager prefers this over `FamilyName` when wiring `FontFamily`. |
| `SubfamilyName` | `name` ID 17, else ID 2 | Style within a family: `Regular`, `Bold Italic`, `SemiBold Condensed`. |
| `FullName` | `name` ID 4 | Family + subfamily concatenation the foundry recommends, e.g. `Inter Bold Italic`. |
| `PostScriptName` | `name` ID 6 | Hyphenated `Family-Style`. Identifier used by PostScript/PDF embedding. ASCII only by convention. |
| `UniqueId` | `name` ID 3 | A globally unique identifier per face — typically `Version <ver>;Vendor;<PostScriptName>`. |
| `Version` | `name` ID 5 | Marketing/legal version string, e.g. `Version 4.001;OTF`. Often more informative than the numeric `FontRevision`. |
| `FontRevision` | `head.fontRevision` | The numeric font revision (Fixed 16.16). Fontager prints it with three fractional digits. |
| `Designer`, `DesignerUrl` | `name` IDs 9 / 12 | The human(s) behind the design. |
| `Manufacturer`, `ManufacturerUrl` | `name` IDs 8 / 11 | The foundry or distribution channel. |
| `Vendor` | `OS/2.achVendID` | Four-character vendor tag. The canonical registry is at <https://learn.microsoft.com/en-us/typography/vendors/>. |
| `Description` | `name` ID 10 | Free-form description authored by the foundry. |
| `SampleText` | `name` ID 19 | Suggested preview text. Useful for non-Latin fonts where "The quick brown fox" is meaningless. |
| `Copyright` | `name` ID 0 | Copyright notice. |
| `Trademark` | `name` ID 7 | Trademark notice. |
| `License`, `LicenseUrl` | `name` IDs 13 / 14 | Long-form license text and a canonical URL (e.g. SIL OFL on OFL.txt). |

### Picking the family name DirectWrite expects

For XAML `FontFamily` we use `TypographicFamilyName` (name ID 16) when it
exists, falling back to `FamilyName`. This matters for icon and CJK fonts
where the basic family name encodes the style (`Inter Bold` instead of
`Inter`) — DirectWrite then refuses to match because there is no
"Inter Bold" *family*, only "Inter" with subfamily "Bold".

---

## 2. Style

The OpenType spec carries style information in three places that should
agree but often don't. Fontager exposes all three so anomalies are
visible.

| Property | Source | Notes |
|---|---|---|
| `Weight` | `OS/2.usWeightClass` | 100…1000 numeric weight. Fontager's `Weight` widget maps to standard names (Thin / Light / Regular / Medium / SemiBold / Bold / ExtraBold / Black). |
| `Width` | `OS/2.usWidthClass` | 1 (Ultra-condensed) … 9 (Ultra-expanded). |
| `IsItalic` | `OS/2.fsSelection` bit 0 | Slanted form with characteristic glyph substitution. |
| `IsOblique` | `OS/2.fsSelection` bit 9 | Slanted form via mechanical slant (no glyph redesign). |
| `MacStyle` | `head.macStyle` | Decoded as a comma-separated label list: `Bold, Italic, Condensed`. Older shells use this; modern apps use `OS/2.fsSelection`. |
| `IsFixedPitch` | `post.isFixedPitch` | Non-zero ⇒ all glyphs share advance width (monospace). The Glyphs grid uses this hint to lay out fixed-width cells. |
| `Classification` | `OS/2.sFamilyClass` + PANOSE family + filename heuristics | Fontager's own bucket (`SansSerif` / `Serif` / `Monospace` / `Display` / `Script` / `Symbol`). |
| `Panose` | `OS/2.panose[0..9]` | Ten-byte design classification hyphen-joined: `2-11-5-2-4-2-4-2-2-4`. Granular but rarely set above the family byte. |
| `ItalicAngle` | `post.italicAngle` | Counter-clockwise degrees from vertical. `0.00` for upright; `-12.00` is a typical italic slant. |

---

## 3. Vertical metrics

The font ships **three** sets of ascender/descender numbers. They exist
because Apple, Microsoft, and the OpenType typographic group can't agree.
Fontager surfaces all three because every text engine picks differently
and font bugs almost always live in their disagreement.

| Property | Source | Used by | Notes |
|---|---|---|---|
| `UnitsPerEm` | `head.unitsPerEm` | Everyone | Design grid resolution. 1000 (CFF), 1024, or 2048 (TrueType) are typical. All metrics below are in these units; divide by `UnitsPerEm` to get fractions of the line height. |
| `XMin`/`YMin`/`XMax`/`YMax` | `head` | Layout fallback | Bounding box of every glyph. |
| `TypoAscender` / `TypoDescender` / `TypoLineGap` | `OS/2.sTypoAscender/sTypoDescender/sTypoLineGap` | Pages, InDesign, Word's *typographic* mode | The "design" line. The descender is negative. Foundries set these to the visually balanced line height they intend. |
| `WinAscent` / `WinDescent` | `OS/2.usWinAscent` / `usWinDescent` | Windows GDI text clipping | The clipping box. Often larger than the typographic line to leave room for tall accents. |
| `HheaAscender` / `HheaDescender` / `HheaLineGap` | `hhea.ascender/descender/lineGap` | macOS, iOS, Safari | Apple's preferred line metrics. |
| `XHeight` | `OS/2.sxHeight` | Optical sizing | Height of `x`. Used to pick a complementary fall-back font. |
| `CapHeight` | `OS/2.sCapHeight` | Optical sizing | Height of capital letters. |
| `UnderlinePosition`, `UnderlineThickness` | `post.underlinePosition/Thickness` | Hyperlinks, rules | Where to draw the underline relative to the baseline. Negative = below baseline. |

---

## 4. Variable fonts

Variable fonts (a.k.a. OpenType Font Variations, `fvar`) ship a single
binary that can render across a continuous design space. Each font
declares one or more **axes**.

`FontMetadata.IsVariable` is `true` exactly when the file has an `fvar`
table. `FontMetadata.Axes` is a list of:

```text
VariationAxis(Tag, Name, Min, Default, Max)
```

Typical axes:

| Tag | Standard meaning | Typical range |
|---|---|---|
| `wght` | Weight | 100 … 900 |
| `wdth` | Width | 50 … 200 (percent) |
| `slnt` | Slant | -15 … 0 (degrees) |
| `ital` | Italic | 0 / 1 |
| `opsz` | Optical size | 6 … 144 (points) |

Custom axes are common — Recursive ships `MONO`, `CRSV`, `CASL`, `EXPR`.
The `Tag` is the canonical identifier; `Name` is the human-readable
axis-name string from the `name` table referenced by the axis record.

> The viewer does not yet expose an interactive axis picker. The
> `Axes` list is currently displayed read-only in the Info tab; an axis
> slider UI is on the roadmap.

---

## 5. OpenType Layout features (GSUB / GPOS)

The `GSUB` and `GPOS` tables advertise *features* — switchable typographic
transformations addressable by a four-character tag. Fontager surfaces the
list of declared tags for each table.

| Tag | Effect | Table |
|---|---|---|
| `liga` | Standard ligatures (`fi` → `ﬁ`) | GSUB |
| `dlig` | Discretionary ligatures (`ct` → `c͡t`) | GSUB |
| `kern` | Kerning between glyph pairs | GPOS |
| `mark` | Mark-to-base attachment (combining accents) | GPOS |
| `mkmk` | Mark-to-mark attachment | GPOS |
| `frac` | Fractions (`1/2` → `½`) | GSUB |
| `sups`/`subs` | Superscripts / subscripts | GSUB |
| `ss01`…`ss20` | Stylistic sets (e.g. alternate `a`) | GSUB |
| `cv01`…`cv99` | Character variants (more granular) | GSUB |
| `smcp`/`c2sc` | Small caps | GSUB |
| `tnum`/`pnum` | Tabular vs. proportional numerals | GSUB |
| `onum`/`lnum` | Old-style vs. lining numerals | GSUB |

A useful rule of thumb: **if you only see `kern` and `mark` in GPOS, the
font has no stylistic depth**. Fonts like Inter or Recursive list 50+
GSUB tags; UI fonts often have nothing but `liga` and `kern`.

The full registered-feature list is at
<https://learn.microsoft.com/en-us/typography/opentype/spec/featuretags>.

---

## 6. Coverage

| Property | Source | Notes |
|---|---|---|
| `GlyphCount` | `maxp.numGlyphs` | Total glyphs in the font (including the `.notdef` slot 0 and any unmapped glyphs reachable only via GSUB substitution). |
| `SupportedCodePoints` | `cmap` | The exact set of Unicode code points the font maps to a glyph. Drives the Glyphs tab — we no longer hard-code "Basic Latin + Latin-1" ranges, we display only what the font actually supports. |

`GlyphCount` is usually larger than `SupportedCodePoints.Count` because
GSUB substitution glyphs (ligatures, small caps, alternates) have glyph
indices but no Unicode mapping. They are reachable only via shaping.

---

## 7. Embedding policy

`EmbeddingRights` decodes `OS/2.fsType` into a label:

| Label | `fsType` bits | Meaning |
|---|---|---|
| `Installable` | 0 | The font may be embedded and installed on the recipient's machine. |
| `Restricted` | bit 1 | No embedding. Copyright violation to even subset. |
| `Preview & Print` | bit 2 | Recipient may view and print, but not edit, the document. |
| `Editable` | bit 3 | Recipient may edit; new content may be authored with the font. |
| `Installable (no subset)` | bit 8 + 9 | Embedding allowed but the document cannot subset the font. |

Plus two independent flags reported alongside:

* `no subsetting` (bit 8) — must embed the whole file.
* `bitmap-only embedding` (bit 9) — only bitmap glyphs may be embedded.

`EmbeddingFlags` is the raw integer so callers can inspect bits we
haven't named explicitly.

---

## 8. Dates

| Property | Source | Notes |
|---|---|---|
| `Created` | `head.created` | When the font was first built. Stored as seconds since 1904-01-01 UTC and printed as `yyyy-MM-dd HH:mm:ssZ`. Sometimes truncated to date-only by tooling. |
| `Modified` | `head.modified` | Last build timestamp. |

Foundries often zero or back-date these for archival reasons; treat them
as advisory.

---

## 9. Format & file

These come from the file itself rather than from a table.

| Property | Source | Notes |
|---|---|---|
| `Format` | File extension + sfnt header sniff | `TrueType` (`.ttf`), `OpenType` (`.otf` with CFF outlines), `TrueTypeCollection` (`.ttc`), `WebOpenFont` (`.woff2`). |
| `FileSize` | OS | Raw byte size. WOFF2 files report the compressed size; the decoded SFNT is typically 2–4× larger. |
| `FilePath` | OS | Absolute path. |
| `FontCount` / `FontIndex` | `ttcf` header for collections | TTCs (Helvetica Neue, Songti) bundle multiple faces into one binary; navigation arrows in the viewer step between them. |

---

## 10. Tips for reading the Info tab

* **Width disagreement** — `WinAscent + WinDescent` ≠ `TypoAscender + |TypoDescender| + TypoLineGap` is the norm. Compare the three vertical groups when a font looks "tight" or "loose" in some browser but not others.
* **PANOSE all zeros** — `0-0-0-0-0-0-0-0-0-0` means the foundry didn't fill in the classification. Don't trust derived properties (e.g. `Classification`) — fall back to the filename / family heuristic.
* **No GPOS `kern`, only `kern` table** — some older fonts ship a legacy `kern` table only (no GPOS). Fontager doesn't currently surface the standalone `kern` table; this is on the roadmap.
* **Variable fonts and weight** — `Weight` from `OS/2.usWeightClass` reflects the *default instance*. The full range lives in the `wght` axis under `Axes`.
