// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Xunit;

// Valid rows on either side of a product whose high word consumes an unrelated
// carry. That product must remain intact and must not disable the other rows.
public class MixedCarryRowsTests
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MulAdd(ulong m, ulong x, ulong t, ulong c, out ulong lo)
    {
        ulong hi = Math.BigMul(m, x, out ulong l);
        l += t; hi += l < t ? 1UL : 0UL;
        l += c; hi += l < c ? 1UL : 0UL;
        lo = l; return hi;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Mixed(ulong[] a, ulong m, ulong[] output, out ulong mix)
    {
        ulong a0 = a[0], a1 = a[1], a2 = a[2], a3 = a[3], a4 = a[4], a5 = a[5];
        ulong r0 = a[6], r1 = a[7], r2 = a[8], r3 = a[9], r4 = a[10], r5 = a[11];
        ulong c = MulAdd(m, a0, r0, 0, out r0);
        c = MulAdd(m, a1, r1, c, out r1);
        c = MulAdd(m, a2, r2, c, out r2);
        c = MulAdd(m, a3, r3, c, out r3);
        c = MulAdd(m, a4, r4, c, out r4);
        c = MulAdd(m, a5, r5, c, out r5);
        ulong firstCarry = c;
        ulong diagonalHigh = Math.BigMul(a0, a0, out ulong diagonalLow);
        ulong sum = r0 + r1;
        diagonalHigh += sum < r0 ? 1UL : 0UL;
        c = MulAdd(m, a5, r0, 0, out r0);
        c = MulAdd(m, a4, r1, c, out r1);
        c = MulAdd(m, a3, r2, c, out r2);
        c = MulAdd(m, a2, r3, c, out r3);
        c = MulAdd(m, a1, r4, c, out r4);
        c = MulAdd(m, a0, r5, c, out r5);
        // Drain both high words before the bounds check on output. This also
        // makes the common multiplier live after both rows in the same block.
        mix = firstCarry ^ c ^ diagonalLow ^ diagonalHigh ^ a0 ^ a1 ^ a2 ^ a3 ^ a4 ^ a5 ^ m;
        output[0] = r0; output[1] = r1; output[2] = r2; output[3] = r3; output[4] = r4; output[5] = r5;
    }

    [Fact]
    public static void TestEntryPoint()
    {
        var random = new Random(134039);
        BigInteger w = BigInteger.One << 64, mask = w - 1, r = BigInteger.One << 384;
        ulong Next() => random.Next(4) switch { 0 => 0, 1 => ulong.MaxValue, _ => (ulong)random.NextInt64() * 3 + 1 };
        for (int test = 0; test < 20000; test++)
        {
            ulong[] a = { Next(), Next(), Next(), Next(), Next(), Next(), Next(), Next(), Next(), Next(), Next(), Next() };
            ulong m = Next(); ulong[] output = new ulong[6]; Mixed(a, m, output, out ulong mix);
            BigInteger x = 0, y = 0, t = 0;
            for (int i = 5; i >= 0; i--) { x = x * w + a[i]; y = y * w + a[5 - i]; t = t * w + a[6 + i]; }
            BigInteger first = t + x * m;
            BigInteger lowSum = (first & mask) + ((first >> 64) & mask);
            BigInteger square = (BigInteger)a[0] * a[0];
            BigInteger second = first % r + y * m;
            for (int i = 0; i < 6; i++)
                if (output[i] != (ulong)((second >> (64 * i)) & mask)) throw new Exception("mixed row limb");
            ulong expected = (ulong)(first / r) ^ (ulong)(second / r) ^ (ulong)(square & mask) ^
                (ulong)(((square >> 64) + lowSum / w) & mask) ^ m;
            for (int i = 0; i < 6; i++) expected ^= a[i];
            if (mix != expected) throw new Exception("mixed product or carry");
        }
    }
}
