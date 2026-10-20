// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using Xunit;

public static unsafe class Arm64LateFlags
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Add(ulong a, ulong b, ref ulong c)
    {
        ulong s = a + b, k = s < a ? 1UL : 0UL, t = s + c;
        c = k + (t < s ? 1UL : 0UL);
        return t;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong LateFlags(ulong a, ulong b, ulong c, ulong z)
    {
        ulong t = Add(a, b, ref c);
        return (z ^ t) + c;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong LateFlags2(ulong a, ulong b, ulong c, ulong z)
    {
        ulong s = a + b, k = s < a ? 1UL : 0UL, t = s + c;
        return (z ^ t) + k + (t < s ? 1UL : 0UL);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint LateFlags32(uint a, uint b, uint c, uint z)
    {
        uint s = a + b, k = s < a ? 1U : 0U, t = s + c;
        return (z ^ t) + k + (t < s ? 1U : 0U);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong LateBorrow(ulong a, ulong b, ulong c, ulong z)
    {
        ulong s = a - b, k = a < b ? 1UL : 0UL, t = s - c;
        return (z ^ t) - k - (s < c ? 1UL : 0UL);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint LateBorrow32(uint a, uint b, uint c, uint z)
    {
        uint s = a - b, k = a < b ? 1U : 0U, t = s - c;
        return (z ^ t) - k - (s < c ? 1U : 0U);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong LateStore(ulong a, ulong b, ulong c, ulong z, ulong* r)
    {
        ulong t = Add(a, b, ref c);
        r[0] = t; r[1] = c;
        return (z ^ t) + c;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong LateFlags3(ulong a0, ulong b0, ulong a1, ulong b1, ulong z, ulong* r)
    {
        ulong c = 0;
        r[0] = Add(a0, b0, ref c);
        ulong t = Add(a1, b1, ref c);
        return (z ^ t) + c;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong LateProduct(ulong a, ulong x, ulong c)
    {
        x += a; c += x < a ? 1UL : 0UL;
        ulong high = Math.BigMul(a, x, out ulong low), s = low + a;
        high += s < low ? 1UL : 0UL;
        ulong result = s + c;
        c = high + (result < s ? 1UL : 0UL);
        return result ^ c;
    }

    [Fact]
    public static int TestEntryPoint()
    {
        ulong[] values = { 0, 1, 2, 3, uint.MaxValue, 1UL << 32, 1UL << 63, ulong.MaxValue - 1, ulong.MaxValue };
        ulong* r = stackalloc ulong[2];
        for (int n = 0; n < 100; n++)
        foreach (ulong a in values)
        foreach (ulong b in values)
        foreach (ulong c in values)
        {
            ulong z = values[(n + 3) % values.Length];
            UInt128 sum = (UInt128)a + b + c;
            ulong expected = unchecked((z ^ (ulong)sum) + (ulong)(sum >> 64));
            if (LateFlags(a, b, c, z) != expected || LateFlags2(a, b, c, z) != expected)
                throw new Exception("Late ADD flags overwrote a forwarded carry");
            if (LateStore(a, b, c, z, r) != expected || r[0] != (ulong)sum || r[1] != (ulong)(sum >> 64))
                throw new Exception("Late carry store");
            UInt128 first = (UInt128)a + b, second = (UInt128)c + z + (ulong)(first >> 64);
            ulong expectedChain = unchecked(((a ^ b) ^ (ulong)second) + (ulong)(second >> 64));
            if (LateFlags3(a, b, c, z, a ^ b, r) != expectedChain || r[0] != (ulong)first)
                throw new Exception("Late carry in two-limb chain");
            ulong productCarry = unchecked(c + (ulong)(first >> 64));
            UInt128 product = (UInt128)a * (ulong)first + a + productCarry;
            if (LateProduct(a, b, c) != ((ulong)product ^ (ulong)(product >> 64)))
                throw new Exception("Late product carry");
            UInt128 biased = ((UInt128)2 << 64) + a - b - c;
            ulong borrow = 2 - (ulong)(biased >> 64);
            if (LateBorrow(a, b, c, z) != unchecked((z ^ (ulong)biased) - borrow))
                throw new Exception("Late SUB flags overwrote a forwarded borrow");
            uint aa = (uint)a, bb = (uint)b, cc = (uint)c, zz = (uint)z;
            ulong sum32 = (ulong)aa + bb + cc;
            uint expected32 = unchecked((zz ^ (uint)sum32) + (uint)(sum32 >> 32));
            if (LateFlags32(aa, bb, cc, zz) != expected32) throw new Exception("Late uint carry");
            ulong biased32 = (2UL << 32) + aa - bb - cc;
            uint borrow32 = 2 - (uint)(biased32 >> 32);
            if (LateBorrow32(aa, bb, cc, zz) != unchecked((zz ^ (uint)biased32) - borrow32))
                throw new Exception("Late uint borrow");
        }
        return 100;
    }
}
