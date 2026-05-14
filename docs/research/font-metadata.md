# Font metadata: what lives inside a font file

> A reference for the tables Fontager parses (and a roadmap for the
> tables it does not yet). Per-property documentation lives in
> [`font-properties.md`](./font-properties.md); this file is organised
> by **file structure**.

OpenType, TrueType, OpenType Collection (`.ttc`), and Web Open Font
Format 2 (`.woff2`) all share the same logical layout: a **directory of
tables**, where each table is a binary blob with a four-character tag.
The fastest way to understand a font binary is to walk the directory
once and reach into the tables you care about.

This document covers:

1. The file container — SFNT / TTC / WOFF2.
2. Each individual table Fontager reads, and what it's used for.
3. Tables Fontager **doesn't** read yet, with notes on what they'd buy us.

The authoritative reference for every byte is the OpenType spec:
<https://learn.microsoft.com/en-us/typography/opentype/spec/otff>.

---

## 1. The container

### 1.1 SFNT (`.ttf` / `.otf`)

```
+-----------------------------------+
| OffsetTable (12 bytes)            |
|   sfntVersion  uint32             |
|   numTables    uint16             |
|   searchRange / entrySelector /   |
|   rangeShift   uint16 x 3         |
+-----------------------------------+
| TableRecord[numTables] (16 b ea.) |
|   tag        char[4]              |
|   checksum   uint32               |
|   offset     uint32  (from BOF)   |
|   length     uint32               |
+-----------------------------------+
| Table data, 4-byte aligned        |
| ...                               |
+-----------------------------------+
```

`sfntVersion` is `0x00010000` for TrueType outlines or `'OTTO'`
(`0x4F54544F`) for CFF/CFF2-based fonts. The directory entries point
back into the same byte buffer at absolute offsets.

### 1.2 TTC (TrueType Collection — `.ttc`)

```
+-----------------------------------+
| TTCHeader                         |
|   ttcTag       'ttcf'             |
|   majorVersion / minorVersion     |
|   numFonts     uint32             |
|   offsetTable  uint32 x numFonts  |
+-----------------------------------+
```

Each entry in `offsetTable` is a byte offset to a normal SFNT
OffsetTable inside the same file. Faces share storage by pointing at the
same table data. Fontager treats each face as its own logical font and
shows index navigation in the viewer header.

### 1.3 WOFF2 (`.woff2`)

WOFF2 is a Brotli-compressed wrapper around SFNT, with **lossless
transformations** applied to the `glyf` and `loca` tables before
compression. The structure is:

```
+-----------------------------------+
| WOFF2Header (48 bytes)            |
|   signature 'wOF2'                |
|   flavor       (uint32) — original sfntVersion
|   length / totalSfntSize / totalCompressedSize
|   numTables / metaOffset / privOffset ...
+-----------------------------------+
| TableDirectory (variable size)    |
|   per entry:                      |
|     flags                  (uint8)
|     [tag if 0x3F]          (char[4])
|     origLength             (UIntBase128)
|     [transformLength]      (UIntBase128, optional)
+-----------------------------------+
| Brotli-compressed payload         |
|   concatenated table data,        |
|   transformed where applicable    |
+-----------------------------------+
| Optional extended metadata + private data
+-----------------------------------+
```

Fontager re-implements the WOFF2 decoder in
[`Fontager.Core/Helpers/Woff2Decoder.cs`](../../Fontager.Core/Helpers/Woff2Decoder.cs)
because DirectWrite's XAML resolver does not transparently decode WOFF2
files on disk — without the decoder, WOFF2 previews fall back to the
system font. The decoder reproduces:

* Header + table-directory parsing (UIntBase128 lengths, the 63-entry
  standard tag table).
* Brotli decompression via the built-in
  `System.IO.Compression.BrotliStream`.
* `glyf` reconstruction via the seven-stream layout (nContour /
  nPoints / flag / glyph / composite / bbox / instruction) and the
  triplet-encoded x/y deltas from Annex C of the WOFF2 spec.
* `loca` reconstruction from the per-glyph offsets.
* SFNT re-serialisation with computed `searchRange` / `entrySelector` /
  `rangeShift` and table checksums.

Not yet implemented: the optional `hmtx` transform (rare in practice;
only a tiny size win when applicable) and the `overlapSimpleBitmap`
field added in WOFF2 1.0.

---

## 2. Tables Fontager reads

The viewer's Info tab is populated from these tables. Offsets below are
relative to each table's start.

### 2.1 `name` — strings

Multi-platform multi-language string store. Layout:

```
uint16 format
uint16 count                  — number of NameRecords
uint16 stringOffset           — bytes from start of name table to storage
NameRecord[count]
   uint16 platformID
   uint16 encodingID
   uint16 languageID
   uint16 nameID
   uint16 length              — bytes
   uint16 stringOffset        — bytes into the storage area
String storage (raw bytes)
```

Per `NameRecord`, the same `nameID` (e.g. 1 = family) may appear under
many `(platform, encoding, language)` combinations. Fontager scores
records and keeps the best, prefering Windows / Unicode BMP / US English
(`platformID=3, encodingID=1, languageID=0x0409`) and falling back to
Mac Roman, then any Unicode-platform record.

The name IDs Fontager extracts:

| ID | Property |
|---|---|
| 0 | Copyright |
| 1 | Family name |
| 2 | Subfamily |
| 3 | Unique identifier |
| 4 | Full font name |
| 5 | Version |
| 6 | PostScript name |
| 7 | Trademark |
| 8 | Manufacturer |
| 9 | Designer |
| 10 | Description |
| 11 | Manufacturer URL |
| 12 | Designer URL |
| 13 | License description |
| 14 | License URL |
| 16 | Typographic family |
| 17 | Typographic subfamily |
| 19 | Sample text |

### 2.2 `head` — face header

```
Fixed   version
Fixed   fontRevision           (16.16 → "%.3f")
uint32  checkSumAdjustment
uint32  magicNumber            (0x5F0F3CF5)
uint16  flags
uint16  unitsPerEm             — design grid resolution
LONGDATETIME created / modified — seconds since 1904-01-01 UTC
int16   xMin / yMin / xMax / yMax — global bounding box
uint16  macStyle               — bold/italic/underline/outline bits
uint16  lowestRecPPEM
int16   fontDirectionHint
int16   indexToLocFormat       — 0 = short loca, 1 = long loca
int16   glyphDataFormat
```

`indexToLocFormat` is what tells the parser whether the `loca` table
uses `uint16` (offsets/2) or `uint32` entries.

### 2.3 `OS/2` — Microsoft Typography metadata

The "metadata for non-print engines" table. Fontager reads:

| Offset | Field | Use |
|---:|---|---|
| 0 | `version` uint16 | Gates which trailing fields exist. |
| 4 | `usWeightClass` uint16 | 100…1000. |
| 6 | `usWidthClass` uint16 | 1…9. |
| 8 | `fsType` uint16 | Embedding rights. |
| 32 | `panose[10]` | Family/serif/weight/etc. classification. |
| 58 | `achVendID` char[4] | Foundry tag. |
| 62 | `fsSelection` uint16 | Italic (bit 0), Oblique (bit 9), Bold (bit 5), etc. |
| 68 | `sTypoAscender` int16 | Preferred typo line. |
| 70 | `sTypoDescender` int16 | Negative. |
| 72 | `sTypoLineGap` int16 | Gap above ascender, below descender. |
| 74 | `usWinAscent` uint16 | Win clipping. |
| 76 | `usWinDescent` uint16 | Win clipping (positive). |
| 86 | `sxHeight` int16 (v2+) | Height of `x`. |
| 88 | `sCapHeight` int16 (v2+) | Cap height. |

Older `OS/2` versions stop earlier; the parser guards every read with a
table-length check.

### 2.4 `hhea` — horizontal header

Pulls the macOS-flavoured ascender/descender:

```
Fixed  version
int16  ascender
int16  descender
int16  lineGap
... (also numberOfHMetrics at offset 34, used by hmtx)
```

### 2.5 `post` — PostScript metadata

```
Fixed   version
Fixed   italicAngle             (counter-clockwise degrees; 0 = upright)
int16   underlinePosition
int16   underlineThickness
uint32  isFixedPitch            — non-zero ⇒ monospace
```

Versions 2.0 and 2.5 add per-glyph PostScript names; Fontager doesn't
read those yet.

### 2.6 `maxp` — maximum profile

A single field of interest:

```
Fixed   version
uint16  numGlyphs
```

(The TrueType `maxp` version 1.0 has many more storage-limit fields;
Fontager only needs the glyph count.)

### 2.7 `cmap` — Unicode → glyph index

The character map. The table can contain several **subtables** keyed by
`(platformID, encodingID)`; Fontager picks the highest-scoring one and
walks it:

| Score | (platformID, encodingID) | Description |
|---|---|---|
| 100 | (3, 10) | Windows / Unicode full repertoire (preferred for non-BMP). |
| 95 | (0, 6) | Unicode platform / Unicode full. |
| 90 | (0, 4) | Unicode 2.0+ full. |
| 80 | (3, 1) | Windows / Unicode BMP. |
| 70 | (0, 3) | Unicode 2.0+ BMP. |
| 60 | (0, 1) | Unicode 1.1. |
| 10 | (1, 0) | Mac Roman (last resort). |

The subtable formats Fontager understands:

* **Format 0** — byte index. 256 entries; ancient Mac fonts.
* **Format 4** — segmented BMP. The classic format for Western TTFs.
* **Format 6** — trimmed table-mapping. Rare.
* **Format 12** — segmented coverage for full-Unicode (incl. SMP/SIP
  planes — emoji, CJK extension A/B). Required for any font with code
  points above U+FFFF.

The output is a `HashSet<int>` of mapped code points. The Glyphs grid
iterates this set; we no longer hard-code "Basic Latin + Latin-1 +
Latin-Ext-A" ranges as we did pre-refactor.

### 2.8 `fvar` — variation axes

```
uint16 majorVersion / minorVersion
uint16 offsetToAxesArray
uint16 (reserved)
uint16 axisCount
uint16 axisSize                  — 20 in v1
uint16 instanceCount
uint16 instanceSize              — 4 + axisCount * 4
```

The axes array contains `axisCount` records of `axisSize` bytes:

```
char[4] axisTag                  — 'wght', 'wdth', 'slnt', 'opsz', custom...
Fixed   minValue / defaultValue / maxValue
uint16  flags
uint16  axisNameID               — into the name table
```

Fontager exposes the resolved name + tag + min/default/max. Instances
(predefined named locations in the design space, like "SemiBold
Condensed") are not yet surfaced.

### 2.9 `GSUB` / `GPOS` — OpenType Layout features

The Layout tables both start with:

```
Fixed   version
uint16  scriptListOffset
uint16  featureListOffset
uint16  lookupListOffset
[uint32 featureVariationsOffset, if version >= 1.1]
```

The `FeatureList` is:

```
uint16  featureCount
FeatureRecord[featureCount]
   char[4] tag
   uint16  featureOffset
```

Fontager harvests the distinct `tag` values and reports them as a list.
Full feature-table parsing — lookups, lookup substitutions, parameter
sub-records like the `cv01` UI labels — is intentionally out of scope:
"what features does this font advertise" answers 95% of UI questions
without a 2000-line GSUB walker.

---

## 3. Tables Fontager does **not** read

Recorded so future work has a roadmap. Tags listed in the spec's
"OpenType Layout common table formats" are nested under GSUB/GPOS and
not separate entries here.

### 3.1 `glyf` + `loca` (TrueType outlines)

Per-glyph outline geometry. Fontager already reconstructs both for
WOFF2 decoding but doesn't surface the geometry to the UI. A future
glyph-detail panel could overlay contour points + control points on
top of the rendered glyph.

### 3.2 `CFF` / `CFF2` (PostScript outlines)

Compact Font Format Type 2 charstrings. Required to read `.otf` glyph
shapes. Same future-work note as `glyf`.

### 3.3 `hmtx` / `vmtx` — horizontal / vertical metrics

Per-glyph advance width and left side bearing. Useful for an "Advance
Width" column in the Glyphs grid or for a kerning-aware preview.

### 3.4 `kern` — legacy kerning

Pre-OpenType kerning. Most modern fonts use GPOS `kern` instead; some
shipped both. Reading the standalone `kern` table would let Fontager
warn when a font has legacy kerning that won't be picked up by
DirectWrite (which prefers GPOS).

### 3.5 `gasp` — grid-fitting / scan-conversion hints

Tells the renderer at what pixel-per-em range hinting / smoothing
should kick in. Useful for diagnosing why a font looks bad at small
sizes.

### 3.6 `COLR` / `CPAL` — colour fonts

Layered colour glyphs (used by Microsoft's Segoe UI Emoji). Surfacing
that a font is a colour font, and previewing the layers, is a future
feature.

### 3.7 `sbix` / `CBDT` / `CBLC` / `SVG ` — bitmap & SVG glyphs

Alternate glyph payloads used by emoji fonts (Apple, Google, Mozilla
respectively). A future emoji-preview mode would need these.

### 3.8 `GDEF` — glyph definitions for shaping

Glyph classes (base / mark / ligature) used by complex-script shapers.
Not strictly metadata but it's how `hb-shape` knows which glyphs are
marks.

### 3.9 `MATH` — math typesetting

OpenType MATH for STIX, Cambria Math, etc.

### 3.10 `meta` — language metadata

The post-OpenType "miscellaneous metadata" table. Holds design language
codes, supported scripts, etc. Rarely populated.

---

## 4. Where this lives in the codebase

* **Container parsing** — `Fontager.Core/Helpers/FontParser.cs` walks
  the SFNT / TTC offset table.
* **WOFF2 → SFNT** — `Fontager.Core/Helpers/Woff2Decoder.cs`.
* **Property model** — `Fontager.Core/Models/FontMetadata.cs` is the
  final shape every consumer (UI + future tooling) sees.
* **Service layer** — `Fontager.Core/Services/FontService.cs` chains
  the WOFF2 decode + parse + filename-fallback so callers have one
  `LoadFontAsync(path)` entry point regardless of format.
* **UI surface** — `Fontager.Viewer/MainWindow.Metadata.cs` renders the
  Info tab from the populated `FontMetadata` record.

---

## 5. Useful references

* OpenType spec (Microsoft): <https://learn.microsoft.com/en-us/typography/opentype/spec/>
* WOFF2 spec (W3C): <https://www.w3.org/TR/WOFF2/>
* PANOSE classification: <https://monotype.github.io/panose/>
* Registered feature tags: <https://learn.microsoft.com/en-us/typography/opentype/spec/featuretags>
* Vendor IDs: <https://learn.microsoft.com/en-us/typography/vendors/>
* Lab to inspect arbitrary fonts: <https://wakamaifondue.com/>
