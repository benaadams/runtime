// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using Xunit;

public static unsafe class Arm64PairedFlags
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StoreCarry(ulong a, ulong b, ulong x, ulong* r)
    {
        r[0] = x;
        ulong s = a + b;
        r[1] = s < a ? 1UL : 0UL;
        r[2] = s;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StoreAdc(ulong a, ulong b, ulong h, ulong x, ulong* r)
    {
        r[0] = x;
        ulong s = a + b;
        r[1] = h + (s < a ? 1UL : 0UL);
        r[2] = s;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StoreCarry32(uint a, uint b, uint x, uint* r)
    {
        r[0] = x;
        uint s = a + b;
        r[1] = s < a ? 1U : 0U;
        r[2] = s;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StoreBorrow(ulong a, ulong b, ulong x, ulong* r)
    {
        r[0] = x;
        ulong d = a - b;
        r[1] = a < b ? 1UL : 0UL;
        r[2] = d;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong LoadCarry(ulong a, ulong b, ulong* p, ulong* r)
    {
        ulong x = p[0], s = a + b;
        ulong y = p[1] + (s < a ? 1UL : 0UL);
        r[0] = s;
        return x ^ y;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong KeepCarry(ulong a, ulong b, ulong h, ulong x, ulong y, ulong* r)
    {
        ulong s = a + b;
        r[0] = s;
        h ^= x;
        ulong high = h + (s < a ? 1UL : 0UL);
        ulong u = x + y;
        r[1] = u < x ? 1UL : 0UL;
        r[2] = u;
        return high;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint KeepCmp6(uint a, uint b, uint h, uint x, uint y, uint* r)
    {
        uint s = a + b;
        r[0] = s;
        h = ~h;
        uint high = h + (s < a ? 1U : 0U);
        r[1] = x < y ? 1U : 0U;
        return (high ^ h) + high;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong KeepCmp7(ulong a, ulong b, ulong h, ulong x, ulong y, ulong* r)
    {
        ulong s = a + b;
        r[0] = s;
        h = ~h;
        ulong high = h + (s < a ? 1UL : 0UL);
        r[1] = x < y ? 1UL : 0UL;
        return (high ^ h) + high;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint KeepSelect32(uint a, uint b, uint h, uint x, uint y, uint* r)
    {
        uint s = a + b;
        r[0] = s;
        h = ~h;
        uint high = h + (s < a ? 1U : 0U);
        r[1] = x < y ? x : y;
        return (high ^ h) + high;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong KeepSelect64(ulong a, ulong b, ulong h, ulong x, ulong y, ulong* r)
    {
        ulong s = a + b;
        r[0] = s;
        h = ~h;
        ulong high = h + (s < a ? 1UL : 0UL);
        r[1] = x < y ? x : y;
        return (high ^ h) + high;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint KeepTest32(uint a, uint b, uint h, uint x, uint y, uint* r)
    {
        uint s = a + b;
        r[0] = s;
        h = ~h;
        uint high = h + (s < a ? 1U : 0U);
        r[1] = (x & y) == 0 ? 1U : 0U;
        return (high ^ h) + high;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong KeepTest64(ulong a, ulong b, ulong h, ulong x, ulong y, ulong* r)
    {
        ulong s = a + b;
        r[0] = s;
        h = ~h;
        ulong high = h + (s < a ? 1UL : 0UL);
        r[1] = (x & y) != 0 ? 1UL : 0UL;
        return (high ^ h) + high;
    }

    [Fact]
    public static int TestEntryPoint()
    {
        ulong[] values = { 0, 1, 2, 7, 0x80000000, 0xffffffff, 1UL << 32, 1UL << 63, ulong.MaxValue };
        ulong* r = stackalloc ulong[3];
        ulong* p = stackalloc ulong[2];
        for (int n = 0; n < 100; n++)
        foreach (ulong a in values)
        foreach (ulong b in values)
        {
            ulong x = unchecked(a + (ulong)n), h = unchecked(b - (ulong)n);
            UInt128 sum = (UInt128)a + b;
            ulong c = (ulong)(sum >> 64);
            StoreCarry(a, b, x, r);
            if (r[0] != x || r[1] != c || r[2] != (ulong)sum) throw new Exception("Paired carry store");
            StoreAdc(a, b, h, x, r);
            if (r[0] != x || r[1] != unchecked(h + c) || r[2] != (ulong)sum) throw new Exception("Paired ADC store");
            StoreBorrow(a, b, x, r);
            if (r[0] != x || r[1] != (a < b ? 1UL : 0UL) || r[2] != unchecked(a - b)) throw new Exception("Paired borrow store");
            uint* r32 = (uint*)r;
            uint aa = (uint)a, bb = (uint)b;
            ulong sum32 = (ulong)aa + bb;
            StoreCarry32(aa, bb, (uint)x, r32);
            if (r32[0] != (uint)x || r32[1] != (uint)(sum32 >> 32) || r32[2] != (uint)sum32) throw new Exception("Paired uint carry store");
            p[0] = x; p[1] = h;
            ulong loaded = LoadCarry(a, b, p, r);
            if (loaded != (x ^ unchecked(h + c)) || r[0] != (ulong)sum) throw new Exception("Paired carry load");
            ulong y = values[(n + 3) % values.Length];
            UInt128 otherSum = (UInt128)x + y;
            ulong kept = KeepCarry(a, b, h, x, y, r);
            if (kept != unchecked((h ^ x) + c) || r[0] != (ulong)sum || r[1] != (ulong)(otherSum >> 64) || r[2] != (ulong)otherSum)
                throw new Exception("Paired store used the previous arithmetic flags");
            kept = KnownAdc(a, b, h, x, y, r);
            ulong knownHigh = unchecked((h ^ x) + c);
            if (kept != knownHigh || r[0] != (ulong)sum || r[1] != unchecked(knownHigh + (ulong)(otherSum >> 64)) || r[2] != (ulong)otherSum)
                throw new Exception("Paired ADC store used known earlier flags");
            kept = KnownBorrow(a, b, h, x, y, r);
            if (kept != knownHigh || r[0] != (ulong)sum || r[1] != (x < y ? 1UL : 0UL) || r[2] != unchecked(x - y))
                throw new Exception("Paired borrow store used known earlier flags");
            uint known32 = KnownCarry32(aa, bb, (uint)h, (uint)x, (uint)y, r32);
            ulong other32 = (ulong)(uint)x + (uint)y;
            if (known32 != unchecked(((uint)h ^ (uint)x) + (uint)(sum32 >> 32)) || r32[0] != (uint)sum32 || r32[1] != (uint)(other32 >> 32) || r32[2] != (uint)other32)
                throw new Exception("Paired uint carry store used known earlier flags");
            uint h32 = ~(uint)h, high32 = unchecked(h32 + (uint)(sum32 >> 32));
            uint expected32 = unchecked((high32 ^ h32) + high32);
            uint cmp32 = KeepCmp6(aa, bb, (uint)h, (uint)x, (uint)y, r32);
            if (cmp32 != expected32 || r32[0] != (uint)sum32 || r32[1] != ((uint)x < (uint)y ? 1U : 0U))
                throw new Exception("Paired uint comparison clobbered carry");
            cmp32 = KeepSelect32(aa, bb, (uint)h, (uint)x, (uint)y, r32);
            if (cmp32 != expected32 || r32[0] != (uint)sum32 || r32[1] != ((uint)x < (uint)y ? (uint)x : (uint)y))
                throw new Exception("Paired uint selection clobbered carry");
            ulong high64 = unchecked(~h + c), expected64 = unchecked((high64 ^ ~h) + high64);
            ulong cmp64 = KeepCmp7(a, b, h, x, y, r);
            if (cmp64 != expected64 || r[0] != (ulong)sum || r[1] != (x < y ? 1UL : 0UL))
                throw new Exception("Paired ulong comparison clobbered carry");
            cmp64 = KeepSelect64(a, b, h, x, y, r);
            if (cmp64 != expected64 || r[0] != (ulong)sum || r[1] != (x < y ? x : y))
                throw new Exception("Paired ulong selection clobbered carry");
            cmp32 = KeepTest32(aa, bb, (uint)h, (uint)x, (uint)y, r32);
            if (cmp32 != expected32 || r32[0] != (uint)sum32 || r32[1] != (((uint)x & (uint)y) == 0 ? 1U : 0U))
                throw new Exception("Paired uint bit test clobbered carry");
            cmp64 = KeepTest64(a, b, h, x, y, r);
            if (cmp64 != expected64 || r[0] != (ulong)sum || r[1] != ((x & y) != 0 ? 1UL : 0UL))
                throw new Exception("Paired ulong bit test clobbered carry");
        }
        return 100;
    }

    // Keep the first carry live through h's update. A wrongly hoisted consumer
    // of the second operation then reads a known in-method producer, so these
    // variants cannot pass merely because the caller supplied convenient NZCV.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong KnownAdc(ulong a, ulong b, ulong h, ulong x, ulong y, ulong* r)
    {
        ulong s = a + b; r[0] = s; h ^= x;
        ulong high = h + (s < a ? 1UL : 0UL);
        ulong u = x + y; r[1] = high + (u < x ? 1UL : 0UL); r[2] = u;
        return high;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong KnownBorrow(ulong a, ulong b, ulong h, ulong x, ulong y, ulong* r)
    {
        ulong s = a + b; r[0] = s; h ^= x;
        ulong high = h + (s < a ? 1UL : 0UL);
        ulong d = x - y; r[1] = x < y ? 1UL : 0UL; r[2] = d;
        return high;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint KnownCarry32(uint a, uint b, uint h, uint x, uint y, uint* r)
    {
        uint s = a + b; r[0] = s; h ^= x;
        uint high = h + (s < a ? 1U : 0U);
        uint u = x + y; r[1] = u < x ? 1U : 0U; r[2] = u;
        return high;
    }
}
