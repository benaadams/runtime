// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System;
using System.Runtime.CompilerServices;
using Xunit;

public static class MaskedSwapTests
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static ulong Select(ulong s, ulong t, ulong mask) => t ^ ((s ^ t) & mask);

    // Two shared XOR deltas, followed by four rollback selects and intervening
    // flag-writing arithmetic: the shape used by the binary-GCD inner loop.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Kernel(ref ulong a, ref ulong b, ref ulong row0, ref ulong row1)
    {
        ulong aa = a, bb = b, r0 = row0, r1 = row1;
        for (int i = 0; i < 31; i++)
        {
            ulong oldA = aa, oldB = bb, old0 = r0, old1 = r1;
            ulong mask = 0UL - (aa < bb ? 1UL : 0UL);
            ulong sa = Select(bb, aa, mask), sb = Select(aa, bb, mask);
            ulong s0 = Select(r1, r0, mask), s1 = Select(r0, r1, mask);
            ulong d = sa - sb, next = s0 - s1 + 0x7fffffff7fffffffUL;
            ulong odd = 0UL - (oldA & 1);
            ulong na = Select(d, oldA, odd), nb = Select(sb, oldB, odd);
            ulong n0 = Select(next, old0, odd), n1 = Select(s1, old1, odd);
            aa = na >> 1; bb = nb; r0 = n0; r1 = 2 * n1 - 0x7fffffff7fffffffUL;
        }
        a = aa; b = bb; row0 = r0; row1 = r1;
    }

    static void Reference(ref ulong a, ref ulong b, ref ulong r0, ref ulong r1)
    {
        for (int i = 0; i < 31; i++)
        {
            if ((a & 1) != 0)
            {
                if (a < b) { (a,b) = (b,a); (r0,r1) = (r1,r0); }
                a -= b; r0 = r0 - r1 + 0x7fffffff7fffffffUL;
            }
            a >>= 1; r1 = 2 * r1 - 0x7fffffff7fffffffUL;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static ulong BitSwap(ulong a, ulong b, ulong bit, out ulong other)
    {
        ulong mask = 0UL - (bit & 1);
        ulong delta = (a ^ b) & mask;
        other = b ^ delta; return a ^ delta;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static uint BitSwap32(uint a, uint b, uint bit, out uint other)
    {
        uint mask = 0U - (bit & 1);
        uint delta = (a ^ b) & mask;
        other = b ^ delta; return a ^ delta;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static uint CompareSwap32(uint a, uint b, out uint other)
    {
        uint mask = 0U - (a >= b ? 1U : 0U), delta = (a ^ b) & mask;
        other = b ^ delta; return a ^ delta;
    }

    // Non-boolean masks must retain their bitwise interpretation.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static ulong ArbitraryMask(ulong a, ulong b, ulong bit, out ulong other)
    {
        ulong mask = 0UL - bit, delta = (a ^ b) & mask;
        other = b ^ delta; return a ^ delta;
    }

    // Escaping delta/mask, a source overwritten between reads, a clobber
    // between outputs, and a live-out delta must not be discarded.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static ulong Escaping(ulong a, ulong b, ulong bit, out ulong delta, out ulong mask)
    { mask = 0UL - (bit & 1); delta = (a ^ b) & mask; return (a ^ delta) + (b ^ delta); }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static ulong Overwrite(ulong a, ulong b, ulong bit)
    {
        ulong mask = 0UL - (bit & 1), delta = (a ^ b) & mask;
        a += 17;
        return (a ^ delta) + (b ^ delta);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static ulong Clobbered(ulong a, ulong b, ulong bit, out ulong other)
    {
        ulong mask = 0UL - (bit & 1), delta = (a ^ b) & mask;
        ulong result = a ^ delta;
        other = (b ^ delta) + (a * b);
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static ulong LiveOut(ulong a, ulong b, ulong bit, bool keep)
    {
        ulong delta = (a ^ b) & (0UL - (bit & 1));
        if (keep) return delta;
        return (a ^ delta) + (b ^ delta);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static ulong Aliased(ref ulong a, ref ulong b, ulong bit)
    {
        ulong delta = (a ^ b) & (0UL - (bit & 1));
        a ^= delta; b ^= delta; return a ^ b;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static ulong Handler(ulong a, ulong b, ulong bit)
    {
        try { ulong delta = (a ^ b) & (0UL - (bit & 1)); return (a ^ delta) + (b ^ delta); }
        catch (Exception) { return a; }
    }

    static void Check(bool condition) { if (!condition) throw new Exception("masked swap mismatch"); }
    [Fact]
    public static int TestEntryPoint()
    {
        ulong[] edges = {0,1,2,3,0x7fffffff,0x80000000,0xffffffff,1UL<<63,ulong.MaxValue};
        Random random = new Random(0x53574150);
        byte[] bytes = new byte[8];
        for (int i = 0; i < 30000; i++)
        {
            random.NextBytes(bytes); ulong a = i < edges.Length ? edges[i] : BitConverter.ToUInt64(bytes);
            random.NextBytes(bytes); ulong b = i < edges.Length ? edges[edges.Length-1-i] : BitConverter.ToUInt64(bytes);
            random.NextBytes(bytes); ulong bit = i < edges.Length ? edges[i] : BitConverter.ToUInt64(bytes);
            ulong x = a, y = b, r0 = bit, r1 = ~bit, ex = x, ey = y, er0 = r0, er1 = r1;
            Kernel(ref x,ref y,ref r0,ref r1); Reference(ref ex,ref ey,ref er0,ref er1);
            Check(x == ex && y == ey && r0 == er0 && r1 == er1);
            ulong mask = 0UL - (bit & 1), delta = (a ^ b) & mask;
            Check(BitSwap(a,b,bit,out ulong other) == (a ^ delta) && other == (b ^ delta));
            uint d32 = ((uint)a ^ (uint)b) & (0U - ((uint)bit & 1));
            Check(BitSwap32((uint)a,(uint)b,(uint)bit,out uint o32) == ((uint)a ^ d32) && o32 == ((uint)b ^ d32));
            Check(CompareSwap32((uint)a,(uint)b,out o32) == Math.Min((uint)a,(uint)b) && o32 == Math.Max((uint)a,(uint)b));
            ulong arbitrary = (a ^ b) & (0UL - bit);
            Check(ArbitraryMask(a,b,bit,out other) == (a ^ arbitrary) && other == (b ^ arbitrary));
            Check(Escaping(a,b,bit,out ulong d,out ulong m) == (a ^ delta) + (b ^ delta) && d == delta && m == mask);
            Check(Overwrite(a,b,bit) == ((a + 17) ^ delta) + (b ^ delta));
            Check(Clobbered(a,b,bit,out other) == (a ^ delta) && other == (b ^ delta) + a*b);
            Check(LiveOut(a,b,bit,true) == delta && LiveOut(a,b,bit,false) == (a ^ delta) + (b ^ delta));
            Check(Handler(a,b,bit) == (a ^ delta) + (b ^ delta));
            x=a; Check(Aliased(ref x,ref x,bit) == 0 && x == a);
        }
        return 100;
    }
}
