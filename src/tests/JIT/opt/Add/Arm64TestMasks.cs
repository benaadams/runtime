// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using Xunit;

public static class Arm64TestMasks
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong Eq64(ulong x, ulong y, ulong a, ulong b, out ulong other)
    {
        ulong mask = 0UL - ((x & y) == 0 ? 1UL : 0UL);
        other = x ^ ((y ^ x) & mask);
        return b ^ ((a ^ b) & mask);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong Ne64(ulong x, ulong y, ulong a, ulong b, out ulong other)
    {
        ulong mask = 0UL - ((x & y) != 0 ? 1UL : 0UL);
        other = x ^ ((y ^ x) & mask);
        return b ^ ((a ^ b) & mask);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint Eq32(uint x, uint y, uint a, uint b, out uint other)
    {
        uint mask = 0U - ((x & y) == 0 ? 1U : 0U);
        other = x ^ ((y ^ x) & mask);
        return b ^ ((a ^ b) & mask);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint Ne32(uint x, uint y, uint a, uint b, out uint other)
    {
        uint mask = 0U - ((x & y) != 0 ? 1U : 0U);
        other = x ^ ((y ^ x) & mask);
        return b ^ ((a ^ b) & mask);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong SwapEq64(ulong x, ulong y, ulong a, ulong b, out ulong other)
    {
        ulong mask = 0UL - ((x & y) == 0 ? 1UL : 0UL);
        ulong delta = (a ^ b) & mask;
        ulong second = (x ^ y) & mask;
        other = (b ^ delta) + (y ^ second);
        return (a ^ delta) + (x ^ second);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong SwapNe64(ulong x, ulong y, ulong a, ulong b, out ulong other)
    {
        ulong mask = 0UL - ((x & y) != 0 ? 1UL : 0UL);
        ulong delta = (a ^ b) & mask;
        ulong second = (x ^ y) & mask;
        other = (b ^ delta) + (y ^ second);
        return (a ^ delta) + (x ^ second);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint SwapEq32(uint x, uint y, uint a, uint b, out uint other)
    {
        uint mask = 0U - ((x & y) == 0 ? 1U : 0U);
        uint delta = (a ^ b) & mask;
        uint second = (x ^ y) & mask;
        other = (b ^ delta) + (y ^ second);
        return (a ^ delta) + (x ^ second);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint SwapNe32(uint x, uint y, uint a, uint b, out uint other)
    {
        uint mask = 0U - ((x & y) != 0 ? 1U : 0U);
        uint delta = (a ^ b) & mask;
        uint second = (x ^ y) & mask;
        other = (b ^ delta) + (y ^ second);
        return (a ^ delta) + (x ^ second);
    }

    [Fact]
    public static int TestEntryPoint()
    {
        ulong[] inputs = { 0, 1, 2, 3, 7, 0x80000000, 0xffffffff, 1UL << 32, 1UL << 63, ulong.MaxValue };
        for (int iteration = 0; iteration < 100; iteration++)
        foreach (ulong x in inputs)
        foreach (ulong y in inputs)
        {
            ulong a = 17UL + (ulong)iteration, b = 42UL + x;
            bool eq = (x & y) == 0;
            ulong r = Eq64(x, y, a, b, out ulong other);
            if (r != (eq ? a : b) || other != (eq ? y : x)) throw new Exception("TEST_EQ select64");
            r = Ne64(x, y, a, b, out other);
            if (r != (eq ? b : a) || other != (eq ? x : y)) throw new Exception("TEST_NE select64");
            r = SwapEq64(x, y, a, b, out other);
            if (r != unchecked(eq ? b + y : a + x) || other != unchecked(eq ? a + x : b + y)) throw new Exception("TEST_EQ swap64");
            r = SwapNe64(x, y, a, b, out other);
            if (r != unchecked(eq ? a + x : b + y) || other != unchecked(eq ? b + y : a + x)) throw new Exception("TEST_NE swap64");
            uint xx = (uint)x, yy = (uint)y, aa = (uint)a, bb = (uint)b;
            eq = (xx & yy) == 0;
            uint rr = Eq32(xx, yy, aa, bb, out uint other32);
            if (rr != (eq ? aa : bb) || other32 != (eq ? yy : xx)) throw new Exception("TEST_EQ select32");
            rr = Ne32(xx, yy, aa, bb, out other32);
            if (rr != (eq ? bb : aa) || other32 != (eq ? xx : yy)) throw new Exception("TEST_NE select32");
            rr = SwapEq32(xx, yy, aa, bb, out other32);
            if (rr != unchecked(eq ? bb + yy : aa + xx) || other32 != unchecked(eq ? aa + xx : bb + yy)) throw new Exception("TEST_EQ swap32");
            rr = SwapNe32(xx, yy, aa, bb, out other32);
            if (rr != unchecked(eq ? aa + xx : bb + yy) || other32 != unchecked(eq ? bb + yy : aa + xx)) throw new Exception("TEST_NE swap32");
        }
        return 100;
    }
}
