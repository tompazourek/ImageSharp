// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;

namespace SixLabors.ImageSharp.Formats.WebP.Lossless
{
    /// <summary>
    /// Utility functions related to creating the huffman tables.
    /// </summary>
    internal static class HuffmanUtils
    {
        public const int HuffmanTableBits = 8;

        public const int HuffmanPackedBits = 6;

        public const int HuffmanTableMask = (1 << HuffmanTableBits) - 1;

        public const uint HuffmanPackedTableSize = 1u << HuffmanPackedBits;

        public static int BuildHuffmanTable(Span<HuffmanCode> table, int rootBits, int[] codeLengths, int codeLengthsSize)
        {
            Guard.MustBeGreaterThan(rootBits, 0, nameof(rootBits));
            Guard.NotNull(codeLengths, nameof(codeLengths));
            Guard.MustBeGreaterThan(codeLengthsSize, 0, nameof(codeLengthsSize));

            // sorted[codeLengthsSize] is a pre-allocated array for sorting symbols by code length.
            var sorted = new int[codeLengthsSize];
            int totalSize = 1 << rootBits; // total size root table + 2nd level table.
            int len; // current code length.
            int symbol; // symbol index in original or sorted table.
            var counts = new int[WebPConstants.MaxAllowedCodeLength + 1]; // number of codes of each length.
            var offsets = new int[WebPConstants.MaxAllowedCodeLength + 1]; // offsets in sorted table for each length.

            // Build histogram of code lengths.
            for (symbol = 0; symbol < codeLengthsSize; ++symbol)
            {
                var codeLengthOfSymbol = codeLengths[symbol];
                if (codeLengthOfSymbol > WebPConstants.MaxAllowedCodeLength)
                {
                    return 0;
                }

                counts[codeLengthOfSymbol]++;
            }

            // Error, all code lengths are zeros.
            if (counts[0] == codeLengthsSize)
            {
                return 0;
            }

            // Generate offsets into sorted symbol table by code length.
            offsets[1] = 0;
            for (len = 1; len < WebPConstants.MaxAllowedCodeLength; ++len)
            {
                int codesOfLength = counts[len];
                if (codesOfLength > (1 << len))
                {
                    return 0;
                }

                offsets[len + 1] = offsets[len] + codesOfLength;
            }

            // Sort symbols by length, by symbol order within each length.
            for (symbol = 0; symbol < codeLengthsSize; ++symbol)
            {
                int symbolCodeLength = codeLengths[symbol];
                if (symbolCodeLength > 0)
                {
                    sorted[offsets[symbolCodeLength]++] = symbol;
                }
            }

            // Special case code with only one value.
            if (offsets[WebPConstants.MaxAllowedCodeLength] == 1)
            {
                var huffmanCode = new HuffmanCode()
                {
                    BitsUsed = 0,
                    Value = (uint)sorted[0]
                };
                ReplicateValue(table, 1, totalSize, huffmanCode);
                return totalSize;
            }

            int step; // step size to replicate values in current table
            int low = -1;     // low bits for current root entry
            int mask = totalSize - 1;    // mask for low bits
            int key = 0;      // reversed prefix code
            int numNodes = 1;     // number of Huffman tree nodes
            int numOpen = 1;      // number of open branches in current tree level
            int tableBits = rootBits;        // key length of current table
            int tableSize = 1 << tableBits;  // size of current table
            symbol = 0;

            // Fill in root table.
            for (len = 1, step = 2; len <= rootBits; ++len, step <<= 1)
            {
                var countsLen = counts[len];
                numOpen <<= 1;
                numNodes += numOpen;
                numOpen -= counts[len];
                if (numOpen < 0)
                {
                    return 0;
                }

                for (; countsLen > 0; countsLen--)
                {
                    var huffmanCode = new HuffmanCode()
                    {
                        BitsUsed = len,
                        Value = (uint)sorted[symbol++]
                    };
                    ReplicateValue(table.Slice(key), step, tableSize, huffmanCode);
                    key = GetNextKey(key, len);
                }

                counts[len] = countsLen;
            }

            // Fill in 2nd level tables and add pointers to root table.
            Span<HuffmanCode> tableSpan = table;
            int tablePos = 0;
            for (len = rootBits + 1, step = 2; len <= WebPConstants.MaxAllowedCodeLength; ++len, step <<= 1)
            {
                numOpen <<= 1;
                numNodes += numOpen;
                numOpen -= counts[len];
                if (numOpen < 0)
                {
                    return 0;
                }

                for (; counts[len] > 0; --counts[len])
                {
                    if ((key & mask) != low)
                    {
                        tableSpan = tableSpan.Slice(tableSize);
                        tablePos += tableSize;
                        tableBits = NextTableBitSize(counts, len, rootBits);
                        tableSize = 1 << tableBits;
                        totalSize += tableSize;
                        low = key & mask;
                        uint v = (uint)(tablePos - low);
                        table[low] = new HuffmanCode
                        {
                            BitsUsed = tableBits + rootBits,
                            Value = (uint)(tablePos - low)
                        };
                    }

                    var huffmanCode = new HuffmanCode
                    {
                        BitsUsed = len - rootBits,
                        Value = (uint)sorted[symbol++]
                    };
                    ReplicateValue(tableSpan.Slice(key >> rootBits), step, tableSize, huffmanCode);
                    key = GetNextKey(key, len);
                }
            }

            return totalSize;
        }

        /// <summary>
        /// Returns the table width of the next 2nd level table. count is the histogram of bit lengths for the remaining symbols,
        /// len is the code length of the next processed symbol.
        /// </summary>
        private static int NextTableBitSize(int[] count, int len, int rootBits)
        {
            int left = 1 << (len - rootBits);
            while (len < WebPConstants.MaxAllowedCodeLength)
            {
                left -= count[len];
                if (left <= 0)
                {
                    break;
                }

                ++len;
                left <<= 1;
            }

            return len - rootBits;
        }

        /// <summary>
        /// Stores code in table[0], table[step], table[2*step], ..., table[end-step].
        /// Assumes that end is an integer multiple of step.
        /// </summary>
        private static void ReplicateValue(Span<HuffmanCode> table, int step, int end, HuffmanCode code)
        {
            Guard.IsTrue(end % step == 0, nameof(end), "end must be a multiple of step");

            do
            {
                end -= step;
                table[end] = code;
            }
            while (end > 0);
        }

        /// <summary>
        /// Returns reverse(reverse(key, len) + 1, len), where reverse(key, len) is the
        /// bit-wise reversal of the len least significant bits of key.
        /// </summary>
        private static int GetNextKey(int key, int len)
        {
            int step = 1 << (len - 1);
            while ((key & step) != 0)
            {
                step >>= 1;
            }

            return step != 0 ? (key & (step - 1)) + step : key;
        }
    }
}