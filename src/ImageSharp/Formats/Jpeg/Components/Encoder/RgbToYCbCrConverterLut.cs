// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Jpeg.Components.Encoder
{
    /// <summary>
    /// Provides 8-bit lookup tables for converting from Rgb to YCbCr colorspace.
    /// Methods to build the tables are based on libjpeg implementation.
    /// </summary>
    internal unsafe struct RgbToYCbCrConverterLut
    {
        /// <summary>
        /// The red luminance table
        /// </summary>
        public fixed int YRTable[256];

        /// <summary>
        /// The green luminance table
        /// </summary>
        public fixed int YGTable[256];

        /// <summary>
        /// The blue luminance table
        /// </summary>
        public fixed int YBTable[256];

        /// <summary>
        /// The red blue-chrominance table
        /// </summary>
        public fixed int CbRTable[256];

        /// <summary>
        /// The green blue-chrominance table
        /// </summary>
        public fixed int CbGTable[256];

        /// <summary>
        /// The blue blue-chrominance table
        /// B=>Cb and R=>Cr are the same
        /// </summary>
        public fixed int CbBTable[256];

        /// <summary>
        /// The green red-chrominance table
        /// </summary>
        public fixed int CrGTable[256];

        /// <summary>
        /// The blue red-chrominance table
        /// </summary>
        public fixed int CrBTable[256];

        // Speediest right-shift on some machines and gives us enough accuracy at 4 decimal places.
        private const int ScaleBits = 16;

        private const int CBCrOffset = 128 << ScaleBits;

        private const int Half = 1 << (ScaleBits - 1);

        /// <summary>
        /// Initializes the YCbCr tables
        /// </summary>
        /// <returns>The initialized <see cref="RgbToYCbCrConverterLut"/></returns>
        public static RgbToYCbCrConverterLut Create()
        {
            RgbToYCbCrConverterLut tables = default;

            for (int i = 0; i <= 255; i++)
            {
                // The values for the calculations are left scaled up since we must add them together before rounding.
                tables.YRTable[i] = Fix(0.299F) * i;
                tables.YGTable[i] = Fix(0.587F) * i;
                tables.YBTable[i] = (Fix(0.114F) * i) + Half;
                tables.CbRTable[i] = (-Fix(0.168735892F)) * i;
                tables.CbGTable[i] = (-Fix(0.331264108F)) * i;

                // We use a rounding fudge - factor of 0.5 - epsilon for Cb and Cr.
                // This ensures that the maximum output will round to 255
                // not 256, and thus that we don't have to range-limit.
                //
                // B=>Cb and R=>Cr tables are the same
                tables.CbBTable[i] = (Fix(0.5F) * i) + CBCrOffset + Half - 1;

                tables.CrGTable[i] = (-Fix(0.418687589F)) * i;
                tables.CrBTable[i] = (-Fix(0.081312411F)) * i;
            }

            return tables;
        }

        /// <summary>
        /// Optimized method to allocates the correct y, cb, and cr values to the DCT blocks from the given r, g, b values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ConvertPixelInto(
            int r,
            int g,
            int b,
            ref Block8x8F yResult,
            ref Block8x8F cbResult,
            ref Block8x8F crResult,
            int i)
        {
            // float y = (0.299F * r) + (0.587F * g) + (0.114F * b);
            yResult[i] = (this.YRTable[r] + this.YGTable[g] + this.YBTable[b]) >> ScaleBits;

            // float cb = 128F + ((-0.168736F * r) - (0.331264F * g) + (0.5F * b));
            cbResult[i] = (this.CbRTable[r] + this.CbGTable[g] + this.CbBTable[b]) >> ScaleBits;

            // float cr = 128F + ((0.5F * r) - (0.418688F * g) - (0.081312F * b));
            crResult[i] = (this.CbBTable[r] + this.CrGTable[g] + this.CrBTable[b]) >> ScaleBits;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ConvertPixelInto(
            int r,
            int g,
            int b,
            ref Block8x8F yResult,
            int i)
        {
            // float y = (0.299F * r) + (0.587F * g) + (0.114F * b);
            yResult[i] = (this.YRTable[r] + this.YGTable[g] + this.YBTable[b]) >> ScaleBits;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ConvertPixelInto(int r, int g, int b, ref float yResult) =>
            // float y = (0.299F * r) + (0.587F * g) + (0.114F * b);
            yResult = (this.YRTable[r] + this.YGTable[g] + this.YBTable[b]) >> ScaleBits;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ConvertPixelInto(int r, int g, int b, ref float cbResult, ref float crResult)
        {
            // float cb = 128F + ((-0.168736F * r) - (0.331264F * g) + (0.5F * b));
            cbResult = (this.CbRTable[r] + this.CbGTable[g] + this.CbBTable[b]) >> ScaleBits;

            // float cr = 128F + ((0.5F * r) - (0.418688F * g) - (0.081312F * b));
            crResult = (this.CbBTable[r] + this.CrGTable[g] + this.CrBTable[b]) >> ScaleBits;
        }

        /// <summary>
        /// Converts Rgb24 pixels into YCbCr color space with 4:4:4 subsampling sampling of luminance and chroma.
        /// </summary>
        /// <param name="rgbSpan">Span of Rgb24 pixel data</param>
        /// <param name="yBlock">Resulting Y values block</param>
        /// <param name="cbBlock">Resulting Cb values block</param>
        /// <param name="crBlock">Resulting Cr values block</param>
        public void Convert444(Span<Rgb24> rgbSpan, ref Block8x8F yBlock, ref Block8x8F cbBlock, ref Block8x8F crBlock)
        {
            ref Rgb24 rgbStart = ref rgbSpan[0];

            for (int i = 0; i < Block8x8F.Size; i++)
            {
                ref Rgb24 c = ref Unsafe.Add(ref rgbStart, i);

                this.ConvertPixelInto(
                    c.R,
                    c.G,
                    c.B,
                    ref yBlock,
                    ref cbBlock,
                    ref crBlock,
                    i);
            }
        }

        /// <summary>
        /// Converts Rgb24 pixels into YCbCr color space with 4:2:0 subsampling of luminance and chroma.
        /// </summary>
        /// <remarks>Calculates 2 out of 4 luminance blocks and half of chroma blocks. This method must be called twice per 4x 8x8 DCT blocks with different row param.</remarks>
        /// <param name="rgbSpan">Span of Rgb24 pixel data</param>
        /// <param name="yBlockLeft">First or "left" resulting Y block</param>
        /// <param name="yBlockRight">Second or "right" resulting Y block</param>
        /// <param name="cbBlock">Resulting Cb values block</param>
        /// <param name="crBlock">Resulting Cr values block</param>
        /// <param name="row">Row index of the 16x16 block, 0 or 1</param>
        public void Convert420(Span<Rgb24> rgbSpan, ref Block8x8F yBlockLeft, ref Block8x8F yBlockRight, ref Block8x8F cbBlock, ref Block8x8F crBlock, int row)
        {
            DebugGuard.MustBeBetweenOrEqualTo(row, 0, 1, nameof(row));

            ref float yBlockLeftRef = ref Unsafe.As<Block8x8F, float>(ref yBlockLeft);
            ref float yBlockRightRef = ref Unsafe.As<Block8x8F, float>(ref yBlockRight);

            // 0-31 or 32-63
            // upper or lower part
            int chromaWriteOffset = row * Block8x8F.Size / 2;
            ref float cbBlockRef = ref Unsafe.Add(ref Unsafe.As<Block8x8F, float>(ref cbBlock), chromaWriteOffset);
            ref float crBlockRef = ref Unsafe.Add(ref Unsafe.As<Block8x8F, float>(ref crBlock), chromaWriteOffset);

            ref Rgb24 rgbStart = ref rgbSpan[0];

            for (int i = 0; i < 8; i += 2)
            {
                // 8 pixels by 3 integers
                Span<int> rgbTriplets = stackalloc int[24];

                for (int j = 0; j < 2; j++)
                {
                    // left
                    ref Rgb24 stride = ref Unsafe.Add(ref rgbStart, (i + j) * 16);
                    for (int k = 0; k < 8; k += 2)
                    {
                        ref float yBlockRef = ref Unsafe.Add(ref yBlockLeftRef, (i + j) * 8 + k);

                        Rgb24 px0 = Unsafe.Add(ref stride, k);
                        Rgb24 px1 = Unsafe.Add(ref stride, k + 1);

                        this.ConvertPixelInto(px0.R, px0.G, px0.B, ref yBlockRef);
                        this.ConvertPixelInto(px1.R, px1.G, px1.B, ref Unsafe.Add(ref yBlockRef, 1));

                        int idx = 3 * (k / 2);
                        rgbTriplets[idx] += px0.R + px1.R;
                        rgbTriplets[idx + 1] += px0.G + px1.G;
                        rgbTriplets[idx + 2] += px0.B + px1.B;
                    }

                    // right
                    stride = ref Unsafe.Add(ref stride, 8);
                    for (int k = 0; k < 8; k += 2)
                    {
                        ref float yBlockRef = ref Unsafe.Add(ref yBlockRightRef, (i + j) * 8 + k);

                        Rgb24 px0 = Unsafe.Add(ref stride, k);
                        Rgb24 px1 = Unsafe.Add(ref stride, k + 1);

                        this.ConvertPixelInto(px0.R, px0.G, px0.B, ref yBlockRef);
                        this.ConvertPixelInto(px1.R, px1.G, px1.B, ref Unsafe.Add(ref yBlockRef, 1));

                        int idx = 3 * (4 + (k / 2));
                        rgbTriplets[idx] += px0.R + px1.R;
                        rgbTriplets[idx + 1] += px0.G + px1.G;
                        rgbTriplets[idx + 2] += px0.B + px1.B;

                    }
                }

                int writeIdx = 8 * (i / 2);
                for (int j = 0; j < 8; j++)
                {
                    int idx = j * 3;
                    this.ConvertPixelInto(
                        rgbTriplets[idx] / 4,       // r
                        rgbTriplets[idx + 1] / 4,   // g
                        rgbTriplets[idx + 2] / 4,   // b
                        ref Unsafe.Add(ref cbBlockRef, writeIdx + j),
                        ref Unsafe.Add(ref crBlockRef, writeIdx + j));
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Fix(float x)
            => (int)((x * (1L << ScaleBits)) + 0.5F);
    }
}
