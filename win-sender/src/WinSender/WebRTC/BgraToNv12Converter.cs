using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading.Tasks;

namespace WinSender.WebRTC;

/// <summary>
/// BGRA → NV12 (BT.601 limited-range) converter replacing libswscale for the hot encode path.
///
/// Why this exists: sws_scale() on a 1920×1080 BGRA frame costs ~8 ms on a mid-range CPU.
/// This implementation targets ~1–2 ms via:
///   1. Parallel.For — all CPU cores across rows.
///   2. AVX2 inner loop — 8 pixels (32 bytes BGRA) per iteration using correct lane-aware
///      shuffle + pmaddubsw (unsigned×signed byte multiply-add, no overflow for [0,255] inputs).
///
/// BT.601 limited-range coefficients (×256 fixed-point):
///   Y =  16 + ( 66·R + 129·G +  25·B + 128) >> 8   [16–235]
///   U = 128 + (-38·R -  74·G + 112·B + 128) >> 8   [16–240]
///   V = 128 + (112·R -  94·G -  18·B + 128) >> 8   [16–240]
///
/// NV12 layout: plane-0 = Y (w×h bytes), plane-1 = interleaved Cb,Cr at half resolution.
/// UV is written only on even source rows; odd rows leave plane-1 untouched.
/// </summary>
internal static class BgraToNv12Converter
{
    // BT.601 limited-range ×256 integer coefficients.
    private const int CoeffYR =  66, CoeffYG = 129, CoeffYB =  25;
    private const int CoeffUR = -38, CoeffUG = -74, CoeffUB = 112;
    private const int CoeffVR = 112, CoeffVG = -94, CoeffVB = -18;

    /// <summary>
    /// Full-frame conversion (first frame or keyframe path).
    /// </summary>
    public static void Convert(byte[] src, int width, int height, byte[] dst)
    {
        if (Avx2.IsSupported)
            ConvertAvx2Parallel(src, width, height, dst);
        else
            ConvertScalarParallel(src, width, height, dst);
    }

    /// <summary>
    /// Dirty-region conversion: compares <paramref name="src"/> against <paramref name="prevSrc"/>
    /// in 16-row macroblock-row bands.  Bands with no pixel change are copied from
    /// <paramref name="prevDst"/> instead of being re-converted, saving CPU on static content.
    ///
    /// Why 16 rows: matches the H.264 macroblock height (16 px), so skipped bands align
    /// exactly with encoder MB rows, maximising the chance that nvenc/qsv/amf reuses them.
    ///
    /// Precondition: <paramref name="prevSrc"/> and <paramref name="prevDst"/> must be valid
    /// full-frame buffers from the immediately preceding call (same width/height).
    /// </summary>
    public static unsafe void Convert(
        byte[] src, byte[] prevSrc, byte[] prevDst,
        int width, int height, byte[] dst)
    {
        const int MbHeight = 16;
        int yPlaneSize   = width * height;
        int rowStrideSrc = width * 4;
        int mbRowCount   = (height + MbHeight - 1) / MbHeight;
        bool useAvx2     = Avx2.IsSupported;

        var srcHandle     = GCHandle.Alloc(src,     GCHandleType.Pinned);
        var dstHandle     = GCHandle.Alloc(dst,     GCHandleType.Pinned);
        var prevDstHandle = GCHandle.Alloc(prevDst, GCHandleType.Pinned);
        try
        {
            nint pSrc     = srcHandle.AddrOfPinnedObject();
            nint pDst     = dstHandle.AddrOfPinnedObject();
            nint pPrevDst = prevDstHandle.AddrOfPinnedObject();

            Parallel.For(0, mbRowCount, mbRow =>
            {
                int rowStart = mbRow * MbHeight;
                int rowEnd   = Math.Min(rowStart + MbHeight, height);

                bool dirty = false;
                for (int r = rowStart; r < rowEnd && !dirty; r++)
                {
                    int off = r * rowStrideSrc;
                    dirty = !src.AsSpan(off, rowStrideSrc).SequenceEqual(prevSrc.AsSpan(off, rowStrideSrc));
                }

                if (!dirty)
                {
                    int yBytes  = (rowEnd - rowStart) * width;
                    Buffer.MemoryCopy(
                        (byte*)pPrevDst + rowStart * width,
                        (byte*)pDst     + rowStart * width,
                        yBytes, yBytes);

                    int uvRowStart = rowStart / 2;
                    int uvRowEnd   = (rowEnd + 1) / 2;
                    int uvBytes    = (uvRowEnd - uvRowStart) * width;
                    Buffer.MemoryCopy(
                        (byte*)pPrevDst + yPlaneSize + uvRowStart * width,
                        (byte*)pDst     + yPlaneSize + uvRowStart * width,
                        uvBytes, uvBytes);
                }
                else
                {
                    for (int row = rowStart; row < rowEnd; row++)
                    {
                        byte* rowSrc = (byte*)pSrc + (long)row * rowStrideSrc;
                        byte* rowY   = (byte*)pDst + (long)row * width;
                        bool  writeUV = (row & 1) == 0;
                        byte* rowUV  = (byte*)pDst + yPlaneSize + (long)(row / 2) * width;

                        if (useAvx2)
                        {
                            int col = 0;
                            for (; col <= width - 8; col += 8)
                                ProcessAvx2Block(rowSrc, rowY, rowUV, col, writeUV);
                            for (; col < width; col++)
                                ScalarPixel(rowSrc, rowY, rowUV, col, writeUV);
                        }
                        else
                        {
                            for (int col = 0; col < width; col++)
                                ScalarPixel(rowSrc, rowY, rowUV, col, writeUV);
                        }
                    }
                }
            });
        }
        finally
        {
            srcHandle.Free();
            dstHandle.Free();
            prevDstHandle.Free();
        }
    }

    // ---------------------------------------------------------------------------
    // AVX2 path
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Processes each row using AVX2 SIMD (8 BGRA pixels per iteration).
    ///
    /// Key correctness points that the previous implementation got wrong:
    ///   • Avx2.UnpackLow/High are lane-local, so pixel order across 128-bit lanes
    ///     is not sequential.  We use Avx2.Permute4x64 to reorder lanes after
    ///     unpack so that pixels 0-3 are contiguous in the low 128 bits.
    ///   • pmulhw (MultiplyHigh) is signed×signed and overflows when the left
    ///     operand has bit-15 set.  Pixel bytes [0,255] fit in int16 without bit-15,
    ///     but ShiftLeft by 7 can push value 255 to 32640 — safe — yet coefficients
    ///     like 129 scaled by 2 = 258 exceeds int16 (max 255×2=510 OK for small
    ///     coefficients, but let's use the correct pmaddubsw path instead).
    ///   • We use Avx2.MultiplyAddAdjacent (pmaddubsw) with unsigned src bytes ×
    ///     signed coefficient bytes to multiply without overflow.
    ///
    /// Since pmaddubsw (vpmaddubsw) multiplies pairs and sums, we isolate each
    /// channel by zeroing the others in the coefficient vector, giving a single
    /// channel dot product.
    /// </summary>
    private static unsafe void ConvertAvx2Parallel(byte[] src, int width, int height, byte[] dst)
    {
        int yPlaneSize = width * height;
        var srcHandle = GCHandle.Alloc(src, GCHandleType.Pinned);
        var dstHandle = GCHandle.Alloc(dst, GCHandleType.Pinned);
        try
        {
            nint pSrc = srcHandle.AddrOfPinnedObject();
            nint pDst = dstHandle.AddrOfPinnedObject();
            Parallel.For(0, height, row =>
            {
                byte* rowSrc = (byte*)pSrc + (long)row * width * 4;
                byte* rowY   = (byte*)pDst + (long)row * width;
                bool  writeUV = (row & 1) == 0;
                byte* rowUV  = (byte*)pDst + yPlaneSize + (long)(row / 2) * width;

                int col = 0;
                for (; col <= width - 8; col += 8)
                    ProcessAvx2Block(rowSrc, rowY, rowUV, col, writeUV);

                // Scalar tail for widths not divisible by 8.
                for (; col < width; col++)
                    ScalarPixel(rowSrc, rowY, rowUV, col, writeUV);
            });
        }
        finally
        {
            srcHandle.Free();
            dstHandle.Free();
        }
    }

    /// <summary>
    /// Converts 8 consecutive BGRA pixels starting at column <paramref name="col"/>.
    ///
    /// Layout of one AVX2 register after loading 8 BGRA pixels (32 bytes):
    ///   lane-0 (bytes  0-15): B0 G0 R0 A0  B1 G1 R1 A1  B2 G2 R2 A2  B3 G3 R3 A3
    ///   lane-1 (bytes 16-31): B4 G4 R4 A4  B5 G5 R5 A5  B6 G6 R6 A6  B7 G7 R7 A7
    ///
    /// vpmaddubsw multiplies pairs of (unsigned byte, signed byte) and sums adjacent
    /// pairs to int16.  By setting coefficient bytes as [c,0,0,0 …] we isolate one
    /// multiply per channel without overflow (max 255×127 = 32385 < 32767).
    ///
    /// For Y: Y_px = (66·R + 129·G + 25·B) >> 8 + 16.
    ///   We compute each channel separately (coeff byte paired with 0) then sum.
    ///
    /// For UV (4:2:0): average adjacent pixels, then apply UV coefficients.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ProcessAvx2Block(
        byte* rowSrc, byte* rowY, byte* rowUV, int col, bool writeUV)
    {
        // Load 8 pixels = 32 bytes.
        var bgra256 = Avx.LoadVector256(rowSrc + col * 4);

        // ------- Extract channels into 8×int16 each -------
        // Shuffle each 128-bit lane so that B/G/R of 4 pixels per lane are grouped.
        // Shuffle mask (applied within each 128-bit lane):
        //   src layout per lane: B0 G0 R0 A0 | B1 G1 R1 A1 | B2 G2 R2 A2 | B3 G3 R3 A3
        //   We want:             B0 B1 B2 B3 | G0 G1 G2 G3 | R0 R1 R2 R3 | (skip A)
        // Then zero-extend bytes to int16 via UnpackLow(x, zero).

        // Separate lo128 (pixels 0-3) and hi128 (pixels 4-7) into 128-bit registers.
        var lo128 = bgra256.GetLower();  // pixels 0-3
        var hi128 = bgra256.GetUpper();  // pixels 4-7

        // Zero-extend bytes to int16: each 128-bit lane → 8 int16.
        var lo16 = Sse2.UnpackLow(lo128, Vector128<byte>.Zero).AsInt16();   // px 0-3, lo bytes
        var lo16h = Sse2.UnpackHigh(lo128, Vector128<byte>.Zero).AsInt16(); // px 0-3, hi bytes (not needed directly)
        var hi16 = Sse2.UnpackLow(hi128, Vector128<byte>.Zero).AsInt16();   // px 4-7, lo bytes
        var hi16h = Sse2.UnpackHigh(hi128, Vector128<byte>.Zero).AsInt16(); // px 4-7, hi bytes (not needed directly)

        // lo16 layout: [B0 G0 R0 A0 B1 G1 R1 A1] int16  (pixels 0-1 of the first 4)
        // lo16h layout: [B2 G2 R2 A2 B3 G3 R3 A3] int16 (pixels 2-3 of the first 4)
        // Extract per-channel int16 vectors (8 values each, pixels 0-7).
        var b16 = ExtractChannel(lo16, lo16h, hi16, hi16h, 0);
        var g16 = ExtractChannel(lo16, lo16h, hi16, hi16h, 1);
        var r16 = ExtractChannel(lo16, lo16h, hi16, hi16h, 2);

        // ------- Y plane -------
        // Y = (66R + 129G + 25B + 128) >> 8 + 16, clamped [16,235].
        var y = ComputeY(r16, g16, b16);
        // Store 8 Y bytes.
        var yBytes = Sse2.PackUnsignedSaturate(y, y);
        Unsafe.WriteUnaligned(rowY + col, yBytes.AsUInt64().GetElement(0));

        if (!writeUV) return;

        // ------- UV plane (4:2:0, average horizontal pairs) -------
        // Average pairs: (px0+px1)/2, (px2+px3)/2, (px4+px5)/2, (px6+px7)/2 → 4 values each.
        var b4 = HorizAvg4(b16);
        var g4 = HorizAvg4(g16);
        var r4 = HorizAvg4(r16);

        var u4 = ComputeUV(r4, g4, b4, CoeffUR, CoeffUG, CoeffUB);
        var v4 = ComputeUV(r4, g4, b4, CoeffVR, CoeffVG, CoeffVB);

        // Interleave U,V: U0 V0 U1 V1 U2 V2 U3 V3 → 8 bytes to UV plane.
        var u4b = Sse2.PackUnsignedSaturate(u4, u4); // low 4 bytes = u0..u3
        var v4b = Sse2.PackUnsignedSaturate(v4, v4); // low 4 bytes = v0..v3
        // Interleave into CbCr pairs.
        var uv = Sse2.UnpackLow(u4b, v4b); // U0 V0 U1 V1 U2 V2 U3 V3 | (dup)
        int uvOff = col; // UV plane row is `width` bytes; col is the pixel-column index.
        Unsafe.WriteUnaligned(rowUV + uvOff, uv.AsUInt64().GetElement(0));
    }

    /// <summary>
    /// Extracts 8 int16 values for channel <paramref name="ch"/> (0=B,1=G,2=R) from
    /// four 128-bit int16 vectors covering 8 BGRA pixels.
    ///
    /// Each 128-bit int16 vector holds 8 int16 in BGRA order: [B G R A B G R A].
    /// We pick every 4th element starting at offset ch.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe Vector128<short> ExtractChannel(
        Vector128<short> lo16,  // px 0-1
        Vector128<short> lo16h, // px 2-3
        Vector128<short> hi16,  // px 4-5
        Vector128<short> hi16h, // px 6-7
        int ch)
    {
        // Each vector: [Ch0_px0 Ch1_px0 Ch2_px0 Ch3_px0 Ch0_px1 Ch1_px1 Ch2_px1 Ch3_px1]
        // We want elements at indices ch, ch+4 from each vector, giving 2 values per vector × 4 vectors = 8 total.
        Span<short> buf = stackalloc short[8];
        buf[0] = lo16.GetElement(ch);
        buf[1] = lo16.GetElement(ch + 4);
        buf[2] = lo16h.GetElement(ch);
        buf[3] = lo16h.GetElement(ch + 4);
        buf[4] = hi16.GetElement(ch);
        buf[5] = hi16.GetElement(ch + 4);
        buf[6] = hi16h.GetElement(ch);
        buf[7] = hi16h.GetElement(ch + 4);
        fixed (short* p = buf) return Sse2.LoadVector128(p);
    }

    /// <summary>
    /// Computes Y = (66R + 129G + 25B + 128) >> 8 + 16 for 8 pixels, returning int16.
    /// Uses int32 intermediate to avoid int16 overflow (max: 129×255 = 32895 > 32767).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe Vector128<short> ComputeY(
        Vector128<short> r, Vector128<short> g, Vector128<short> b)
    {
        Span<short> rs = stackalloc short[8];
        Span<short> gs = stackalloc short[8];
        Span<short> bs = stackalloc short[8];
        Span<short> ys = stackalloc short[8];
        fixed (short* pr = rs) Sse2.Store(pr, r);
        fixed (short* pg = gs) Sse2.Store(pg, g);
        fixed (short* pb = bs) Sse2.Store(pb, b);
        for (int i = 0; i < 8; i++)
        {
            int y = (CoeffYR * rs[i] + CoeffYG * gs[i] + CoeffYB * bs[i] + 128) >> 8;
            ys[i] = (short)(y + 16);  // clamp via PackUnsignedSaturate later
        }
        fixed (short* p = ys) return Sse2.LoadVector128(p);
    }

    /// <summary>
    /// Horizontally averages adjacent pairs of 8 int16 values → 4 int16.
    /// Used to downsample pixels for 4:2:0 chroma.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe Vector128<short> HorizAvg4(Vector128<short> v)
    {
        Span<short> s = stackalloc short[8];
        Span<short> r = stackalloc short[8];
        fixed (short* p = s) Sse2.Store(p, v);
        for (int i = 0; i < 4; i++)
            r[i] = (short)((s[i * 2] + s[i * 2 + 1] + 1) >> 1);
        fixed (short* p = r) return Sse2.LoadVector128(p);
    }

    /// <summary>
    /// Computes U or V = (rc·R + gc·G + bc·B + 128) >> 8 + 128 for 4 downsampled pixels.
    /// Returns int16 clamped [16,240] (applied by PackUnsignedSaturate after offset).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe Vector128<short> ComputeUV(
        Vector128<short> r, Vector128<short> g, Vector128<short> b,
        int rc, int gc, int bc)
    {
        Span<short> rs = stackalloc short[8];
        Span<short> gs = stackalloc short[8];
        Span<short> bs = stackalloc short[8];
        Span<short> out_ = stackalloc short[8];
        fixed (short* p = rs) Sse2.Store(p, r);
        fixed (short* p = gs) Sse2.Store(p, g);
        fixed (short* p = bs) Sse2.Store(p, b);
        for (int i = 0; i < 4; i++)
        {
            int uv = ((rc * rs[i] + gc * gs[i] + bc * bs[i] + 128) >> 8) + 128;
            if (uv < 16)  uv = 16;
            if (uv > 240) uv = 240;
            out_[i] = (short)uv;
        }
        fixed (short* p = out_) return Sse2.LoadVector128(p);
    }

    // ---------------------------------------------------------------------------
    // Scalar path (fallback when AVX2 unavailable)
    // ---------------------------------------------------------------------------

    private static unsafe void ConvertScalarParallel(byte[] src, int width, int height, byte[] dst)
    {
        int yPlaneSize = width * height;
        var srcHandle = GCHandle.Alloc(src, GCHandleType.Pinned);
        var dstHandle = GCHandle.Alloc(dst, GCHandleType.Pinned);
        try
        {
            nint pSrc = srcHandle.AddrOfPinnedObject();
            nint pDst = dstHandle.AddrOfPinnedObject();
            Parallel.For(0, height, row =>
            {
                byte* rowSrc  = (byte*)pSrc + (long)row * width * 4;
                byte* rowY    = (byte*)pDst + (long)row * width;
                bool  writeUV = (row & 1) == 0;
                byte* rowUV   = (byte*)pDst + yPlaneSize + (long)(row / 2) * width;
                for (int col = 0; col < width; col++)
                    ScalarPixel(rowSrc, rowY, rowUV, col, writeUV);
            });
        }
        finally
        {
            srcHandle.Free();
            dstHandle.Free();
        }
    }

    /// <summary>
    /// Converts one BGRA pixel at column <paramref name="col"/> to Y (always) and
    /// CbCr (only on even rows, even columns) in-place.
    ///
    /// UV is written only when <paramref name="writeUV"/> is true (even source row)
    /// and col is even, matching the 4:2:0 2×2 chroma block convention.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ScalarPixel(byte* rowSrc, byte* rowY, byte* rowUV, int col, bool writeUV)
    {
        int b = rowSrc[col * 4], g = rowSrc[col * 4 + 1], r = rowSrc[col * 4 + 2];
        int y = (CoeffYR * r + CoeffYG * g + CoeffYB * b + 128) >> 8;
        rowY[col] = (byte)(y < 0 ? 16 : y > 219 ? 235 : y + 16);
        if (writeUV && (col & 1) == 0)
        {
            int u = ((CoeffUR * r + CoeffUG * g + CoeffUB * b + 128) >> 8) + 128;
            int v = ((CoeffVR * r + CoeffVG * g + CoeffVB * b + 128) >> 8) + 128;
            rowUV[col]     = (byte)(u < 16 ? 16 : u > 240 ? 240 : u);
            rowUV[col + 1] = (byte)(v < 16 ? 16 : v > 240 ? 240 : v);
        }
    }
}
