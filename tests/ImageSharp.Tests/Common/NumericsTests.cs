// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using Xunit;
using Xunit.Abstractions;

namespace SixLabors.ImageSharp.Tests.Common
{
    public class NumericsTests
    {
        private ITestOutputHelper Output { get; }

        public NumericsTests(ITestOutputHelper output)
        {
            this.Output = output;
        }

        private static int Log2_ReferenceImplementation(uint value)
        {
            int n = 0;
            while ((value >>= 1) != 0)
            {
                ++n;
            }

            return n;
        }

        [Fact]
        public void Log2_ZeroConvention()
        {
            uint value = 0;
            int expected = 0;
            int actual = Numerics.Log2(value);

            Assert.True(expected == actual, $"Expected: {expected}, Actual: {actual}");
        }

        [Fact]
        public void Log2_PowersOfTwo()
        {
            for (int i = 0; i < sizeof(int) * 8; i++)
            {
                // from 2^0 to 2^32
                uint value = (uint)(1 << i);
                int expected = i;
                int actual = Numerics.Log2(value);

                Assert.True(expected == actual, $"Expected: {expected}, Actual: {actual}");
            }
        }

        [Theory]
        [InlineData(1, 100)]
        [InlineData(2, 100)]
        public void Log2_RandomValues(int seed, int count)
        {
            var rng = new Random(seed);
            byte[] bytes = new byte[4];

            for (int i = 0; i < count; i++)
            {
                rng.NextBytes(bytes);
                uint value = BitConverter.ToUInt32(bytes, 0);
                int expected = Log2_ReferenceImplementation(value);
                int actual = Numerics.Log2(value);

                Assert.True(expected == actual, $"Expected: {expected}, Actual: {actual}");
            }
        }
    }
}
