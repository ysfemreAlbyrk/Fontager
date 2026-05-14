using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Fontager.Core.Helpers;

/// <summary>
/// Pure C# WOFF2 → SFNT (TTF/OTF) decompressor. Lets Fontager render the
/// real face of a <c>.woff2</c> file inside XAML — DirectWrite's font
/// resolver does not transparently decode WOFF2 from a <c>file://</c> URI,
/// so without this stage the viewer falls back to the system font.
///
/// <para>
/// Implements the subset of <a href="https://www.w3.org/TR/WOFF2/">W3C WOFF2</a>
/// that real-world fonts actually use:
/// </para>
/// <list type="bullet">
///   <item><description>Header, table directory (with UIntBase128 lengths),
///     Brotli-compressed payload.</description></item>
///   <item><description>Untransformed tables — copied verbatim into the
///     output SFNT.</description></item>
///   <item><description>Transformed <c>glyf</c> + <c>loca</c> — reconstructed
///     from the WOFF2 stream layout (nContour / nPoints / flag / glyph /
///     composite / bbox / instruction streams) using the triplet glyph
///     encoding (Annex C) and the 255UInt16 length encoding (Annex B).</description></item>
///   <item><description>Output SFNT with computed <c>searchRange</c>,
///     <c>entrySelector</c>, <c>rangeShift</c>, and table checksums.</description></item>
/// </list>
///
/// <para>
/// Not implemented (rare in practice — these fall back to the original
/// stream length and may render incorrectly): the optional
/// <c>hmtx</c> transform (left-side-bearing strip) and the
/// <c>overlapSimpleBitmap</c> from the 1.0 spec. Both are signalled in
/// the WOFF2 header and we leave clear hooks for them.
/// </para>
///
/// <para>
/// Throws <see cref="InvalidDataException"/> on malformed input — callers
/// should catch and fall back to system-font rendering.
/// </para>
/// </summary>
public static class Woff2Decoder
{
    /// <summary>WOFF2 magic number: ASCII 'wOF2'.</summary>
    public const uint Signature = 0x774F4632;

    /// <summary>True if <paramref name="bytes"/> begins with the WOFF2 signature.</summary>
    public static bool IsWoff2(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4) return false;
        return BinaryPrimitives.ReadUInt32BigEndian(bytes) == Signature;
    }

    /// <summary>Convenience: peek at the first 4 bytes of a file.</summary>
    public static bool IsWoff2(string filePath)
    {
        try
        {
            Span<byte> head = stackalloc byte[4];
            using var fs = File.OpenRead(filePath);
            int read = fs.Read(head);
            return read == 4 && IsWoff2(head);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reads <paramref name="woff2Path"/>, decompresses it, and returns the
    /// equivalent SFNT (TTF or OTF depending on the source flavor) bytes.
    /// </summary>
    public static byte[] DecodeToSfnt(string woff2Path)
        => DecodeToSfnt(File.ReadAllBytes(woff2Path));

    /// <summary>
    /// If <paramref name="data"/> is a WOFF2 stream, returns the equivalent
    /// SFNT bytes; otherwise returns <paramref name="data"/> unchanged.
    /// Convenience for callers that want one code path regardless of format.
    /// </summary>
    public static byte[] DecodeIfWoff2(byte[] data)
        => IsWoff2(data) ? DecodeToSfnt(data) : data;

    /// <summary>
    /// Returns true if <paramref name="sfntBytes"/> starts with the
    /// <c>'OTTO'</c> magic (CFF-flavoured OpenType). Useful for picking
    /// between <c>.ttf</c> and <c>.otf</c> when writing decoded WOFF2
    /// output back to disk so the file extension reflects the contents.
    /// </summary>
    public static bool IsOpenTypeFlavor(ReadOnlySpan<byte> sfntBytes)
    {
        if (sfntBytes.Length < 4) return false;
        return BinaryPrimitives.ReadUInt32BigEndian(sfntBytes) == 0x4F54544F; // 'OTTO'
    }

    /// <summary>
    /// Decompresses the WOFF2 payload in <paramref name="data"/> into a
    /// well-formed SFNT byte array.
    /// </summary>
    public static byte[] DecodeToSfnt(byte[] data)
    {
        if (!IsWoff2(data))
            throw new InvalidDataException("Not a WOFF2 file (signature mismatch).");

        var header = ReadHeader(data);
        var tables = ReadTableDirectory(data, header);
        var payload = DecompressBrotli(
            data.AsSpan(header.PayloadStart, (int)header.TotalCompressedSize),
            (int)header.TotalSfntSize);

        // Hand each transformed table its slice of the decompressed payload.
        int cursor = 0;
        foreach (var t in tables)
        {
            int len = (int)t.TransformLength;
            if (cursor + len > payload.Length)
                throw new InvalidDataException(
                    $"Decompressed payload truncated for table '{t.Tag}'.");
            t.TransformedData = new byte[len];
            Buffer.BlockCopy(payload, cursor, t.TransformedData, 0, len);
            cursor += len;
        }

        // Reconstruct transformed tables in place. glyf reconstruction
        // also fills the matching loca entry, so we walk the directory
        // once and only act when glyf is encountered.
        foreach (var t in tables)
        {
            if (t.Tag == "glyf" && t.TransformVersion == 0)
            {
                var loca = FindTable(tables, "loca")
                    ?? throw new InvalidDataException("Transformed glyf without companion loca table.");
                ReconstructGlyfAndLoca(t, loca);
            }
        }

        // Anything not explicitly reconstructed is identity-mapped.
        foreach (var t in tables)
            t.OriginalData ??= t.TransformedData;

        var sfnt = BuildSfnt(header.Flavor, tables);
        TryAdjustSfntHeadChecksum(sfnt);
        return sfnt;
    }

    /// <summary>
    /// OpenType requires the whole-font checksum (sum of UInt32 big-endian words)
    /// to equal <c>0xB1B0AFBA</c> once <c>head.checkSumAdjustment</c> is set.
    /// Some loaders ignore an incorrect value; DirectWrite on Windows is stricter
    /// with rebuilt binaries — fixing this avoids silent rejection of decoded WOFF2.
    /// </summary>
    private static void TryAdjustSfntHeadChecksum(byte[] font)
    {
        try
        {
            if (font.Length < 12) return;
            int numTables = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
            if (numTables <= 0 || numTables > 256) return;

            int headOffset = -1;
            for (int ti = 0; ti < numTables; ti++)
            {
                int entry = 12 + ti * 16;
                if (entry + 16 > font.Length) return;
                if (font[entry] == (byte)'h' && font[entry + 1] == (byte)'e'
                    && font[entry + 2] == (byte)'a' && font[entry + 3] == (byte)'d')
                {
                    headOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(entry + 8));
                    break;
                }
            }

            if (headOffset < 0 || headOffset + 12 > font.Length) return;

            BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(headOffset + 8), 0);
            uint sum = ComputeChecksum(font);
            uint adjustment = unchecked(0xB1B0AFBAu - sum);
            BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(headOffset + 8), adjustment);
        }
        catch
        {
            // Best-effort; the font may still load without adjustment on many builds.
        }
    }

    // ── Header / directory ─────────────────────────────────────────────

    private sealed class Woff2Header
    {
        public uint Flavor;
        public uint Length;
        public ushort NumTables;
        public uint TotalSfntSize;
        public uint TotalCompressedSize;
        public int PayloadStart; // byte offset where Brotli stream begins
    }

    private sealed class Woff2Table
    {
        public string Tag = "";
        /// <summary>0 for transformable tables (glyf/loca) when transformation IS applied; 3 for "no transform". For other tables this is just informational.</summary>
        public byte TransformVersion;
        public uint OrigLength;
        public uint TransformLength;
        public byte[] TransformedData = [];
        public byte[]? OriginalData; // populated either from transform reconstruction or by passing through TransformedData
    }

    private static Woff2Header ReadHeader(byte[] data)
    {
        if (data.Length < 48)
            throw new InvalidDataException("WOFF2 header too small.");

        var h = new Woff2Header
        {
            Flavor = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)),
            Length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8)),
            NumTables = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(12)),
            // 2 bytes reserved at offset 14
            TotalSfntSize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16)),
            TotalCompressedSize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20)),
            // majorVersion/minorVersion at 24/26
            // metaOffset (28), metaLength (32), metaOrigLength (36)
            // privOffset (40), privLength (44)
        };

        // PayloadStart is computed after we walk the directory.
        return h;
    }

    /// <summary>Standard WOFF2 table tag index → tag string (spec Table 2).</summary>
    private static readonly string[] s_knownTags =
    [
        "cmap","head","hhea","hmtx","maxp","name","OS/2","post",
        "cvt ","fpgm","glyf","loca","prep","CFF ","VORG","EBDT",
        "EBLC","gasp","hdmx","kern","LTSH","PCLT","VDMX","vhea",
        "vmtx","BASE","GDEF","GPOS","GSUB","EBSC","JSTF","MATH",
        "CBDT","CBLC","COLR","CPAL","SVG ","sbix","acnt","avar",
        "bdat","bloc","bsln","cvar","fdsc","feat","fmtx","fvar",
        "gvar","hsty","just","lcar","mort","morx","opbd","prop",
        "trak","Zapf","Silf","Glat","Gloc","Feat","Sill"
    ];

    private static List<Woff2Table> ReadTableDirectory(byte[] data, Woff2Header header)
    {
        var tables = new List<Woff2Table>(header.NumTables);
        int p = 48;

        for (int i = 0; i < header.NumTables; i++)
        {
            if (p >= data.Length) throw new InvalidDataException("Truncated table directory.");

            byte flags = data[p++];
            int tagIdx = flags & 0x3F;
            byte tv = (byte)((flags >> 6) & 0x03);

            string tag;
            if (tagIdx == 0x3F)
            {
                if (p + 4 > data.Length) throw new InvalidDataException("Truncated custom tag.");
                tag = string.Create(4, (data, p), static (span, state) =>
                {
                    var (d, i) = state;
                    span[0] = (char)d[i];
                    span[1] = (char)d[i + 1];
                    span[2] = (char)d[i + 2];
                    span[3] = (char)d[i + 3];
                });
                p += 4;
            }
            else
            {
                if (tagIdx >= s_knownTags.Length)
                    throw new InvalidDataException($"Unknown WOFF2 tag index {tagIdx}.");
                tag = s_knownTags[tagIdx];
            }

            uint origLen = ReadUIntBase128(data, ref p);
            uint transLen = origLen;

            // glyf (tagIdx=10) and loca (tagIdx=11) have a transformLength when
            // transform is applied (transform_version==0). All other tables
            // also encode a transformLength only when the transform_version
            // is non-zero — but in practice WOFF2 1.0 reserves non-zero
            // transforms for hmtx (version 1). To be safe, mirror the spec:
            //   - tag in {glyf, loca} AND tv == 0  → transformLength present
            //   - otherwise                        → transformLength present iff tv != 0
            bool isGlyfOrLoca = tag == "glyf" || tag == "loca";
            bool hasTransformLength = isGlyfOrLoca ? (tv == 0) : (tv != 0);
            if (hasTransformLength)
            {
                transLen = ReadUIntBase128(data, ref p);
            }

            // loca's transformed payload is zero-length: it's reconstructed
            // from glyf during the rebuild pass.
            tables.Add(new Woff2Table
            {
                Tag = tag,
                TransformVersion = tv,
                OrigLength = origLen,
                TransformLength = transLen,
            });
        }

        header.PayloadStart = p;
        return tables;
    }

    private static Woff2Table? FindTable(List<Woff2Table> tables, string tag)
    {
        foreach (var t in tables) if (t.Tag == tag) return t;
        return null;
    }

    // ── Variable-length integer encodings (Annex A/B) ──────────────────

    /// <summary>
    /// UIntBase128: 1..5 bytes, each with a continuation bit. The high bit
    /// signals "more bytes"; the low 7 are concatenated big-endian. Per the
    /// spec, leading zeros and overflows past 2^32-1 are illegal.
    /// </summary>
    private static uint ReadUIntBase128(byte[] data, ref int p)
    {
        uint value = 0;
        for (int i = 0; i < 5; i++)
        {
            if (p >= data.Length)
                throw new InvalidDataException("Truncated UIntBase128.");
            byte b = data[p++];
            if (i == 0 && b == 0x80)
                throw new InvalidDataException("UIntBase128 leading zero.");
            if ((value & 0xFE000000) != 0)
                throw new InvalidDataException("UIntBase128 overflow.");
            value = (value << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0)
                return value;
        }
        throw new InvalidDataException("UIntBase128 too long.");
    }

    /// <summary>
    /// 255UInt16: 1..3 bytes. Compactly encodes values in [0, 65535].
    /// </summary>
    private static ushort Read255UInt16(byte[] data, ref int p)
    {
        if (p >= data.Length)
            throw new InvalidDataException("Truncated 255UInt16.");
        byte b0 = data[p++];
        if (b0 == 253)
        {
            if (p + 2 > data.Length) throw new InvalidDataException("Truncated 255UInt16 word.");
            ushort v = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p));
            p += 2;
            return v;
        }
        if (b0 == 254)
        {
            if (p >= data.Length) throw new InvalidDataException("Truncated 255UInt16.");
            return (ushort)(data[p++] + 506);
        }
        if (b0 == 255)
        {
            if (p >= data.Length) throw new InvalidDataException("Truncated 255UInt16.");
            return (ushort)(data[p++] + 253);
        }
        return b0;
    }

    // ── Brotli ─────────────────────────────────────────────────────────

    private static byte[] DecompressBrotli(ReadOnlySpan<byte> compressed, int expectedLength)
    {
        // BrotliStream's "uncompressedSize" hint matches the WOFF2 header's
        // totalSfntSize budget pre-reconstruction (i.e. the sum of all
        // transformLengths). Use it to size the output buffer up front and
        // skip List<byte> growth.
        var outBuf = new MemoryStream(expectedLength > 0 ? expectedLength : 16 * 1024);
        using (var input = new MemoryStream(compressed.ToArray(), writable: false))
        using (var brotli = new BrotliStream(input, CompressionMode.Decompress, leaveOpen: false))
        {
            brotli.CopyTo(outBuf);
        }
        return outBuf.ToArray();
    }

    // ── glyf / loca transform reconstruction ───────────────────────────

    /// <summary>
    /// Rebuilds the original <c>glyf</c> and <c>loca</c> tables from the
    /// WOFF2 transformed glyf stream (spec §5.1).
    ///
    /// <para>
    /// The transformed glyf table is a custom binary layout with seven
    /// streams (nContour / nPoints / flag / glyph / composite / bbox /
    /// instruction). For each glyph we re-emit the SFNT glyf entry:
    /// header (numContours, xMin/yMin/xMax/yMax), endPtsOfContours[],
    /// instructionLength, instructions, flags, x/y deltas.
    /// </para>
    /// </summary>
    private static void ReconstructGlyfAndLoca(Woff2Table glyf, Woff2Table loca)
    {
        var src = glyf.TransformedData;
        if (src.Length < 36)
            throw new InvalidDataException("Transformed glyf header too small.");

        int p = 0;
        // version (uint32) — must be 0
        uint version = BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(p)); p += 4;
        if (version != 0)
            throw new InvalidDataException("Unsupported transformed glyf version.");
        ushort optionFlags = BinaryPrimitives.ReadUInt16BigEndian(src.AsSpan(p)); p += 2;
        ushort numGlyphs = BinaryPrimitives.ReadUInt16BigEndian(src.AsSpan(p)); p += 2;
        ushort indexFormat = BinaryPrimitives.ReadUInt16BigEndian(src.AsSpan(p)); p += 2;

        uint nContourStreamSize = BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(p)); p += 4;
        uint nPointsStreamSize = BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(p)); p += 4;
        uint flagStreamSize = BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(p)); p += 4;
        uint glyphStreamSize = BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(p)); p += 4;
        uint compositeStreamSize = BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(p)); p += 4;
        uint bboxStreamSize = BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(p)); p += 4;
        uint instructionStreamSize = BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(p)); p += 4;

        // bboxStreamSize includes both the bitmap (ceil(numGlyphs/8) bytes)
        // and the int16[4] entries that follow.
        int bboxBitmapBytes = (numGlyphs + 7) / 8;

        // Stream offsets.
        int nContourOffset = p; p += (int)nContourStreamSize;
        int nPointsOffset = p; p += (int)nPointsStreamSize;
        int flagOffset = p; p += (int)flagStreamSize;
        int glyphOffset = p; p += (int)glyphStreamSize;
        int compositeOffset = p; p += (int)compositeStreamSize;
        int bboxOffset = p; p += (int)bboxStreamSize;
        int instructionOffset = p; p += (int)instructionStreamSize;

        // Optional 1.0 addition: overlapSimpleBitmap (skipped — informational).
        if ((optionFlags & 0x0001) != 0)
            p += (numGlyphs + 7) / 8;

        int bboxStreamCursor = bboxOffset + bboxBitmapBytes;
        int nPointsCursor = nPointsOffset;
        int flagCursor = flagOffset;
        int glyphCursor = glyphOffset;
        int compositeCursor = compositeOffset;
        int instructionCursor = instructionOffset;

        // glyf and loca outputs are grown incrementally.
        var glyfOut = new MemoryStream(src.Length * 2);
        var locaOffsets = new uint[numGlyphs + 1];

        for (int gi = 0; gi < numGlyphs; gi++)
        {
            locaOffsets[gi] = (uint)glyfOut.Length;

            // nContour is a signed int16: -1 for composite, 0 for empty,
            // positive for simple.
            short nContour = BinaryPrimitives.ReadInt16BigEndian(
                src.AsSpan(nContourOffset + gi * 2));

            // Pull the bbox if the bitmap says so. For composite glyphs the
            // spec REQUIRES the bbox to be present; for simple glyphs it's
            // present only when the bitmap bit is set.
            bool hasBbox = (src[bboxOffset + (gi >> 3)] & (1 << (7 - (gi & 7)))) != 0;

            if (nContour == 0)
            {
                // Empty glyph emits a zero-length entry; loca records the
                // same offset for both ends.
                continue;
            }

            if (nContour > 0)
            {
                // Simple glyph. Read per-contour point counts.
                int totalPoints = 0;
                var endPts = new ushort[nContour];
                for (int c = 0; c < nContour; c++)
                {
                    ushort nPoints = Read255UInt16(src, ref nPointsCursor);
                    totalPoints += nPoints;
                    endPts[c] = (ushort)(totalPoints - 1);
                }

                // Decode flags + triplet x/y deltas.
                var xs = new int[totalPoints];
                var ys = new int[totalPoints];
                var ftAbs = new byte[totalPoints]; // SFNT flag bytes (on-curve flags only here; we keep deltas independent)

                for (int pt = 0; pt < totalPoints; pt++)
                {
                    if (flagCursor >= flagOffset + flagStreamSize)
                        throw new InvalidDataException("flagStream underrun.");
                    byte fb = src[flagCursor++];
                    bool onCurve = (fb & 0x80) == 0;
                    int code = fb & 0x7F;
                    DecodeTriplet(src, ref glyphCursor, code, out int dx, out int dy);
                    xs[pt] = dx;
                    ys[pt] = dy;
                    ftAbs[pt] = (byte)(onCurve ? 0x01 : 0x00); // OnCurve bit only; SFNT flag byte builder below will OR in delta-encoding bits.
                }

                // Read instruction length from glyphStream, then instructions.
                ushort instrLen = Read255UInt16(src, ref glyphCursor);
                if (instructionCursor + instrLen > instructionOffset + instructionStreamSize)
                    throw new InvalidDataException("instructionStream underrun.");
                var instructions = new byte[instrLen];
                Buffer.BlockCopy(src, instructionCursor, instructions, 0, instrLen);
                instructionCursor += instrLen;

                // Compute the bbox if the bbox bitmap doesn't carry one.
                short xMin, yMin, xMax, yMax;
                if (hasBbox)
                {
                    if (bboxStreamCursor + 8 > src.Length)
                        throw new InvalidDataException("bboxStream underrun.");
                    xMin = BinaryPrimitives.ReadInt16BigEndian(src.AsSpan(bboxStreamCursor));
                    yMin = BinaryPrimitives.ReadInt16BigEndian(src.AsSpan(bboxStreamCursor + 2));
                    xMax = BinaryPrimitives.ReadInt16BigEndian(src.AsSpan(bboxStreamCursor + 4));
                    yMax = BinaryPrimitives.ReadInt16BigEndian(src.AsSpan(bboxStreamCursor + 6));
                    bboxStreamCursor += 8;
                }
                else
                {
                    int x = 0, y = 0;
                    int xLo = int.MaxValue, xHi = int.MinValue, yLo = int.MaxValue, yHi = int.MinValue;
                    for (int pt = 0; pt < totalPoints; pt++)
                    {
                        x += xs[pt]; y += ys[pt];
                        if (x < xLo) xLo = x; if (x > xHi) xHi = x;
                        if (y < yLo) yLo = y; if (y > yHi) yHi = y;
                    }
                    if (totalPoints == 0) { xLo = xHi = yLo = yHi = 0; }
                    xMin = (short)Math.Clamp(xLo, short.MinValue, short.MaxValue);
                    xMax = (short)Math.Clamp(xHi, short.MinValue, short.MaxValue);
                    yMin = (short)Math.Clamp(yLo, short.MinValue, short.MaxValue);
                    yMax = (short)Math.Clamp(yHi, short.MinValue, short.MaxValue);
                }

                WriteGlyfHeader(glyfOut, nContour, xMin, yMin, xMax, yMax);

                // endPtsOfContours
                foreach (var ep in endPts) WriteUInt16BE(glyfOut, ep);
                // instructionLength + instructions
                WriteUInt16BE(glyfOut, instrLen);
                glyfOut.Write(instructions, 0, instrLen);

                // SFNT flag bytes + x/y arrays (use plain "no-compression"
                // encoding for the flags so we don't have to dedupe).
                // Flag byte bits we care about:
                //   0x01 = ON_CURVE
                //   0x02 = X_SHORT_VECTOR
                //   0x04 = Y_SHORT_VECTOR
                //   0x10 = X_IS_SAME_OR_POSITIVE_X_SHORT (or x duplicate)
                //   0x20 = Y_IS_SAME_OR_POSITIVE_Y_SHORT
                // We always emit deltas as int16 → both short bits clear.
                foreach (var f in ftAbs)
                    glyfOut.WriteByte((byte)(f & 0x01));

                for (int pt = 0; pt < totalPoints; pt++)
                    WriteInt16BE(glyfOut, (short)Math.Clamp(xs[pt], short.MinValue, short.MaxValue));
                for (int pt = 0; pt < totalPoints; pt++)
                    WriteInt16BE(glyfOut, (short)Math.Clamp(ys[pt], short.MinValue, short.MaxValue));

                AlignToFour(glyfOut);
                continue;
            }

            // Composite glyph (nContour < 0).
            // The composite stream holds the raw component records (same as
            // SFNT). We need to scan them to detect MORE_COMPONENTS and
            // WE_HAVE_INSTRUCTIONS flags so we know where the record ends
            // and whether an instructionLength + instructions follows from
            // the glyph / instruction streams.

            int compositeStart = compositeCursor;
            bool moreComponents = true;
            bool weHaveInstructions = false;
            while (moreComponents)
            {
                if (compositeCursor + 4 > src.Length)
                    throw new InvalidDataException("compositeStream underrun.");
                ushort flags = BinaryPrimitives.ReadUInt16BigEndian(src.AsSpan(compositeCursor)); compositeCursor += 2;
                // glyphIndex
                compositeCursor += 2;

                bool arg1And2AreWords = (flags & 0x0001) != 0;
                bool weHaveAScale = (flags & 0x0008) != 0;
                bool weHaveXY = (flags & 0x0040) != 0;
                bool weHaveTwoByTwo = (flags & 0x0080) != 0;
                weHaveInstructions = weHaveInstructions || ((flags & 0x0100) != 0);
                moreComponents = (flags & 0x0020) != 0;

                int argBytes = arg1And2AreWords ? 4 : 2;
                int transformBytes = 0;
                if (weHaveAScale) transformBytes = 2;
                else if (weHaveXY) transformBytes = 4;
                else if (weHaveTwoByTwo) transformBytes = 8;

                compositeCursor += argBytes + transformBytes;
                if (compositeCursor > compositeOffset + compositeStreamSize)
                    throw new InvalidDataException("compositeStream walked past end.");
            }
            int compositeRecordLen = compositeCursor - compositeStart;

            // bbox is always present for composite glyphs per spec.
            if (!hasBbox || bboxStreamCursor + 8 > src.Length)
                throw new InvalidDataException("Composite glyph missing bbox.");
            short cxMin = BinaryPrimitives.ReadInt16BigEndian(src.AsSpan(bboxStreamCursor));
            short cyMin = BinaryPrimitives.ReadInt16BigEndian(src.AsSpan(bboxStreamCursor + 2));
            short cxMax = BinaryPrimitives.ReadInt16BigEndian(src.AsSpan(bboxStreamCursor + 4));
            short cyMax = BinaryPrimitives.ReadInt16BigEndian(src.AsSpan(bboxStreamCursor + 6));
            bboxStreamCursor += 8;

            WriteGlyfHeader(glyfOut, nContour, cxMin, cyMin, cxMax, cyMax);
            glyfOut.Write(src, compositeStart, compositeRecordLen);

            if (weHaveInstructions)
            {
                ushort instrLen = Read255UInt16(src, ref glyphCursor);
                if (instructionCursor + instrLen > instructionOffset + instructionStreamSize)
                    throw new InvalidDataException("instructionStream underrun (composite).");
                WriteUInt16BE(glyfOut, instrLen);
                glyfOut.Write(src, instructionCursor, instrLen);
                instructionCursor += instrLen;
            }

            AlignToFour(glyfOut);
        }

        locaOffsets[numGlyphs] = (uint)glyfOut.Length;
        glyf.OriginalData = glyfOut.ToArray();

        // Build loca table. indexFormat: 0 = short (uint16, offsets / 2), 1 = long (uint32).
        using var locaStream = new MemoryStream();
        if (indexFormat == 0)
        {
            foreach (var off in locaOffsets)
                WriteUInt16BE(locaStream, (ushort)(off / 2));
        }
        else
        {
            foreach (var off in locaOffsets)
                WriteUInt32BE(locaStream, off);
        }
        loca.OriginalData = locaStream.ToArray();
    }

    // ── Triplet glyph-stream decoding (Annex C) ────────────────────────

    /// <summary>
    /// Decodes one <c>(dx, dy)</c> point delta from the glyphStream using
    /// the 128-entry triplet table from WOFF2 Annex C / spec Table 5.
    ///
    /// <para>
    /// The 128 codes fall into five ranges; within each range the bits of
    /// <c>code - rangeStart</c> select sign, deltaX-base, and deltaY-base.
    /// In every range, bit 0 of the (relative) code is the y-sign and bit
    /// 1 is the x-sign (set = positive, clear = negative — note that this
    /// is inverted from the natural "1 = negative" guess and is the most
    /// common spec-reading bug).
    /// </para>
    /// </summary>
    private static void DecodeTriplet(byte[] data, ref int cursor, int code, out int dx, out int dy)
    {
        if (code < 0 || code > 127)
            throw new InvalidDataException("Invalid triplet code.");

        // Range 1: 0..9 — Y-only delta, 1 data byte, deltaY ∈ {0,256,512,768,1024}.
        if (code < 10)
        {
            int ySign = ((code & 1) != 0) ? +1 : -1;
            int deltaY = (code >> 1) << 8;
            if (cursor >= data.Length) throw new InvalidDataException("triplet underrun (1)");
            int by = data[cursor++];
            dx = 0;
            dy = ySign * (deltaY + by);
            return;
        }

        // Range 2: 10..19 — X-only delta, mirror of range 1.
        if (code < 20)
        {
            int k = code - 10;
            int xSign = ((k & 1) != 0) ? +1 : -1;
            int deltaX = (k >> 1) << 8;
            if (cursor >= data.Length) throw new InvalidDataException("triplet underrun (2)");
            int bx = data[cursor++];
            dx = xSign * (deltaX + bx);
            dy = 0;
            return;
        }

        // Range 3: 20..83 — 1 data byte split into x-high-nibble / y-low-nibble.
        // 4 deltaX × 4 deltaY (each 1 + 16 * idx) × 4 sign combinations.
        if (code < 84)
        {
            int b0 = code - 20;
            int xSign = ((b0 & 0x02) != 0) ? +1 : -1;
            int ySign = ((b0 & 0x01) != 0) ? +1 : -1;
            int ix = (b0 >> 4) & 0x03;
            int iy = (b0 >> 2) & 0x03;
            if (cursor >= data.Length) throw new InvalidDataException("triplet underrun (3)");
            int by = data[cursor++];
            int xLow = (by >> 4) & 0x0F;
            int yLow = by & 0x0F;
            dx = xSign * (1 + 16 * ix + xLow);
            dy = ySign * (1 + 16 * iy + yLow);
            return;
        }

        // Range 4: 84..119 — 2 data bytes, 3 deltaX × 3 deltaY × 4 signs (= 36).
        if (code < 120)
        {
            int b0 = code - 84;
            int xSign = ((b0 & 0x02) != 0) ? +1 : -1;
            int ySign = ((b0 & 0x01) != 0) ? +1 : -1;
            int bigIdx = b0 >> 2;   // 0..8
            int ix = bigIdx / 3;    // 0..2
            int iy = bigIdx % 3;    // 0..2
            if (cursor + 1 >= data.Length) throw new InvalidDataException("triplet underrun (4)");
            int bx = data[cursor++];
            int by = data[cursor++];
            dx = xSign * (1 + 256 * ix + bx);
            dy = ySign * (1 + 256 * iy + by);
            return;
        }

        // Range 5: 120..123 — 12-bit X / 12-bit Y, packed into 3 data bytes.
        if (code < 124)
        {
            int b0 = code - 120;
            int xSign = ((b0 & 0x02) != 0) ? +1 : -1;
            int ySign = ((b0 & 0x01) != 0) ? +1 : -1;
            if (cursor + 2 >= data.Length) throw new InvalidDataException("triplet underrun (5)");
            int b1 = data[cursor++];
            int b2 = data[cursor++];
            int b3 = data[cursor++];
            int xv = (b1 << 4) | (b2 >> 4);
            int yv = ((b2 & 0x0F) << 8) | b3;
            dx = xSign * xv;
            dy = ySign * yv;
            return;
        }

        // Range 6: 124..127 — full 16-bit X / 16-bit Y, 4 data bytes.
        {
            int b0 = code - 124;
            int xSign = ((b0 & 0x02) != 0) ? +1 : -1;
            int ySign = ((b0 & 0x01) != 0) ? +1 : -1;
            if (cursor + 3 >= data.Length) throw new InvalidDataException("triplet underrun (6)");
            int b1 = data[cursor++];
            int b2 = data[cursor++];
            int b3 = data[cursor++];
            int b4 = data[cursor++];
            int xv = (b1 << 8) | b2;
            int yv = (b3 << 8) | b4;
            dx = xSign * xv;
            dy = ySign * yv;
        }
    }

    // ── SFNT output ────────────────────────────────────────────────────

    private static byte[] BuildSfnt(uint flavor, List<Woff2Table> tables)
    {
        // Order tables alphabetically by tag for the directory (SFNT
        // recommends sorted directory entries).
        var sorted = new List<Woff2Table>(tables);
        sorted.Sort((a, b) => string.CompareOrdinal(a.Tag, b.Tag));

        ushort numTables = (ushort)sorted.Count;
        int headerLen = 12 + 16 * numTables;

        // Compute table data offsets (4-byte aligned).
        var offsets = new uint[numTables];
        var lengths = new uint[numTables];
        var checksums = new uint[numTables];

        int cursor = headerLen;
        for (int i = 0; i < numTables; i++)
        {
            var t = sorted[i];
            var data = t.OriginalData ?? t.TransformedData;
            offsets[i] = (uint)cursor;
            lengths[i] = (uint)data.Length;
            checksums[i] = ComputeChecksum(data);
            int padded = (data.Length + 3) & ~3;
            cursor += padded;
        }

        int totalSize = cursor;
        var sfnt = new byte[totalSize];

        // Header.
        ushort entrySelector = (ushort)Math.Floor(Math.Log2(Math.Max(1, (int)numTables)));
        ushort searchRange = (ushort)(16 * (1 << entrySelector));
        ushort rangeShift = (ushort)(16 * numTables - searchRange);

        BinaryPrimitives.WriteUInt32BigEndian(sfnt.AsSpan(0), flavor);
        BinaryPrimitives.WriteUInt16BigEndian(sfnt.AsSpan(4), numTables);
        BinaryPrimitives.WriteUInt16BigEndian(sfnt.AsSpan(6), searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(sfnt.AsSpan(8), entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(sfnt.AsSpan(10), rangeShift);

        // Directory entries + table data.
        for (int i = 0; i < numTables; i++)
        {
            int entry = 12 + 16 * i;
            var t = sorted[i];
            WriteTag(sfnt, entry, t.Tag);
            BinaryPrimitives.WriteUInt32BigEndian(sfnt.AsSpan(entry + 4), checksums[i]);
            BinaryPrimitives.WriteUInt32BigEndian(sfnt.AsSpan(entry + 8), offsets[i]);
            BinaryPrimitives.WriteUInt32BigEndian(sfnt.AsSpan(entry + 12), lengths[i]);

            var data = t.OriginalData ?? t.TransformedData;
            Buffer.BlockCopy(data, 0, sfnt, (int)offsets[i], data.Length);
            // Padding bytes are already zero from the initial array allocation.
        }

        return sfnt;
    }

    private static uint ComputeChecksum(byte[] data)
    {
        uint sum = 0;
        int i = 0;
        for (; i + 4 <= data.Length; i += 4)
        {
            sum += BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i));
        }
        if (i < data.Length)
        {
            // Pad the last word with zeros (only conceptually — we don't
            // mutate the input).
            uint last = 0;
            for (int j = 0; j < 4; j++)
            {
                last <<= 8;
                if (i + j < data.Length) last |= data[i + j];
            }
            sum += last;
        }
        return sum;
    }

    // ── low-level write helpers ────────────────────────────────────────

    private static void WriteUInt16BE(Stream s, ushort v)
    {
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
    }

    private static void WriteInt16BE(Stream s, short v)
        => WriteUInt16BE(s, unchecked((ushort)v));

    private static void WriteUInt32BE(Stream s, uint v)
    {
        s.WriteByte((byte)(v >> 24));
        s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
    }

    private static void WriteGlyfHeader(Stream s, short numContours, short xMin, short yMin, short xMax, short yMax)
    {
        WriteInt16BE(s, numContours);
        WriteInt16BE(s, xMin);
        WriteInt16BE(s, yMin);
        WriteInt16BE(s, xMax);
        WriteInt16BE(s, yMax);
    }

    private static void AlignToFour(MemoryStream s)
    {
        while ((s.Length & 3) != 0) s.WriteByte(0);
    }

    private static void WriteTag(byte[] sfnt, int offset, string tag)
    {
        for (int i = 0; i < 4; i++)
            sfnt[offset + i] = (byte)(i < tag.Length ? tag[i] : ' ');
    }
}
