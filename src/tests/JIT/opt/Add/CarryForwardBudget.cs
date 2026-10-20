// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Xunit;

public class CarryForwardBudgetTests
{
    private readonly struct Words
    {
        public readonly ulong L0, L1, L2, L3, L4, L5;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Words(ulong a, ulong b, ulong c, ulong d, ulong e, ulong f)
        { L0=a; L1=b; L2=c; L3=d; L4=e; L5=f; }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Add(ulong a, ulong b, ref ulong carry)
    {
        ulong s = a + b, c = s < a ? 1UL : 0UL;
        ulong r = s + carry; carry = c + (r < s ? 1UL : 0UL); return r;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddMod(out Words result, in Words a, in Words b)
    {
        ulong carry = 0;
        ulong r0 = Add(a.L0, b.L0, ref carry);
        ulong r1 = Add(a.L1, b.L1, ref carry);
        ulong r2 = Add(a.L2, b.L2, ref carry);
        ulong r3 = Add(a.L3, b.L3, ref carry);
        ulong r4 = Add(a.L4, b.L4, ref carry);
        ulong r5 = Add(a.L5, b.L5, ref carry);
        ulong s0 = r0 + 0x4601000000005555UL;
        carry = s0 < r0 ? 1UL : 0UL;
        ulong s1 = Add(r1, 0xe15400014eac0000UL, ref carry);
        ulong s2 = Add(r2, 0x98cf2d5f094f09dbUL, ref carry);
        ulong s3 = Add(r3, 0x9b88b47b0c7aed40UL, ref carry);
        ulong s4 = Add(r4, 0xb4e45849bcb45328UL, ref carry);
        ulong s5 = Add(r5, 0xe5feee15c6801965UL, ref carry);
        ulong mask = 0 - carry;
        result = new Words(r0 ^ ((r0 ^ s0) & mask), r1 ^ ((r1 ^ s1) & mask), r2 ^ ((r2 ^ s2) & mask), r3 ^ ((r3 ^ s3) & mask), r4 ^ ((r4 ^ s4) & mask), r5 ^ ((r5 ^ s5) & mask));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Words LargeBlock(in Words a, in Words b)
    {
        Words result = a;
        AddMod(out result, result, b);
        AddMod(out result, result, b);
        AddMod(out result, result, b);
        AddMod(out result, result, b);
        AddMod(out result, result, b);
        AddMod(out result, result, b);
        AddMod(out result, result, b);
        AddMod(out result, result, b);
        AddMod(out result, result, b);
        AddMod(out result, result, b);
        AddMod(out result, result, b);
        AddMod(out result, result, b);
        return result;
    }

    [Fact]
    public static void TestEntryPoint()
    {
        var random = new Random(134040);
        BigInteger modulus = BigInteger.Parse("01a0111ea397fe69a4b1ba7b6434bacd764774b84f38512bf6730d2a0f6b0f6241eabfffeb153ffffb9feffffffffaaab", System.Globalization.NumberStyles.HexNumber);
        BigInteger mask = (BigInteger.One << 64) - 1;
        Words Encode(BigInteger x) => new Words((ulong)(x & mask), (ulong)((x >> 64) & mask), (ulong)((x >> 128) & mask),
            (ulong)((x >> 192) & mask), (ulong)((x >> 256) & mask), (ulong)((x >> 320) & mask));
        byte[] bytes = new byte[48];
        for (int test = 0; test < 10000; test++)
        {
            random.NextBytes(bytes); BigInteger x = new BigInteger(bytes, isUnsigned: true) % modulus;
            random.NextBytes(bytes); BigInteger y = new BigInteger(bytes, isUnsigned: true) % modulus;
            if ((test & 7) == 0) x = y = modulus - 1;
            if ((test & 7) == 1) x = y = 0;
            Words a = Encode(x), b = Encode(y); int rounds = test % 4;
            Words actual = a;
            for (int round = 0; round < rounds; round++) actual = LargeBlock(actual, b);
            Words expected = Encode((x + 12 * rounds * y) % modulus);
            if (actual.L0 != expected.L0 || actual.L1 != expected.L1 || actual.L2 != expected.L2 ||
                actual.L3 != expected.L3 || actual.L4 != expected.L4 || actual.L5 != expected.L5)
                throw new Exception("large modular block mismatch");
        }
    }
}
