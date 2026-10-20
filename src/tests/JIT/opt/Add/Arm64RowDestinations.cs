// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using Xunit;

public static unsafe class Arm64RowDestinations
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Accumulate(ulong m, ulong a, ref ulong t, ulong c)
    {
        ulong h = Math.BigMul(m, a, out ulong l);
        t += l; h += t < l ? 1UL : 0UL;
        t += c; h += t < c ? 1UL : 0UL;
        return h;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong RowDest(ulong* a, ulong* t, ulong* r, ulong m, ulong c)
    {
        ulong t0 = t[0], t1 = t[1];
        c = Accumulate(m, a[0], ref t0, c);
        c = Accumulate(m, a[1], ref t1, c);
        r[0] = t0; r[1] = t1;
        return c;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong RowDestParams(ulong m, ulong a0, ulong a1, ulong t0, ulong t1, ulong c, ulong* r)
    {
        ulong h0 = Math.BigMul(m, a0, out ulong l0);
        t0 += l0; h0 += t0 < l0 ? 1UL : 0UL;
        t0 += c; h0 += t0 < c ? 1UL : 0UL;
        ulong h1 = Math.BigMul(m, a1, out ulong l1);
        t1 += l1; h1 += t1 < l1 ? 1UL : 0UL;
        t1 += h0; h1 += t1 < h0 ? 1UL : 0UL;
        r[0] = t0; r[1] = t1;
        return h1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong RowDestReversed(ulong m, ulong a0, ulong a1, ulong t0, ulong t1, ulong c, ulong* r)
    {
        ulong h0 = Math.BigMul(m, a0, out ulong l0);
        t0 = l0 + t0; h0 += t0 < l0 ? 1UL : 0UL;
        t0 = c + t0; h0 += t0 < c ? 1UL : 0UL;
        ulong h1 = Math.BigMul(m, a1, out ulong l1);
        t1 = l1 + t1; h1 += t1 < l1 ? 1UL : 0UL;
        t1 = h0 + t1; h1 += t1 < h0 ? 1UL : 0UL;
        r[0] = t0; r[1] = t1;
        return h1;
    }

    // This order already reads each destination at the second addition.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong RowCarryFirst(ulong m, ulong a0, ulong a1, ulong t0, ulong t1, ulong c, ulong* r)
    {
        ulong h0 = Math.BigMul(m, a0, out ulong l0), s0 = l0 + c;
        h0 += s0 < l0 ? 1UL : 0UL;
        t0 += s0; h0 += t0 < s0 ? 1UL : 0UL;
        ulong h1 = Math.BigMul(m, a1, out ulong l1), s1 = l1 + h0;
        h1 += s1 < l1 ? 1UL : 0UL;
        t1 += s1; h1 += t1 < s1 ? 1UL : 0UL;
        r[0] = t0; r[1] = t1;
        return h1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong RowDestRedef(ulong m, ulong a0, ulong a1, ulong d0, ulong d1, ulong c, ulong* r)
    {
        ulong h0 = Math.BigMul(m, a0, out ulong l0), p0 = l0 + d0;
        h0 += p0 < l0 ? 1UL : 0UL;
        d0 = r[2];
        ulong q0 = p0 + c; h0 += q0 < c ? 1UL : 0UL;
        ulong h1 = Math.BigMul(m, a1, out ulong l1), p1 = l1 + d1;
        h1 += p1 < l1 ? 1UL : 0UL;
        ulong q1 = p1 + h0; h1 += q1 < h0 ? 1UL : 0UL;
        r[0] = q0 + d0; r[1] = q1;
        return h1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong LoopDest(ulong* a, ulong* t, ulong* r, ulong m, ulong c, int n)
    {
        do
        {
            ulong d = t[0], h = Math.BigMul(m, a[0], out ulong l), p = l + d;
            h += p < l ? 1UL : 0UL;
            d = r[1];
            ulong q = p + c; h += q < c ? 1UL : 0UL;
            r[0] = q + d;
            c = h; a++; t++; r++;
        } while (--n != 0);
        return c;
    }

    private static ulong Next(ref ulong state, int index)
    {
        state ^= state << 13; state ^= state >> 7; state ^= state << 17;
        return (index & 7) switch { 0 => 0, 1 => 1, 2 => ulong.MaxValue, 3 => 1UL << 63, _ => state };
    }

    [Fact]
    public static int TestEntryPoint()
    {
        ulong state = 0x123456789abcdef1;
        ulong* a = stackalloc ulong[4];
        ulong* t = stackalloc ulong[4];
        ulong* r = stackalloc ulong[5];
        ulong* expected = stackalloc ulong[5];
        for (int i = 0; i < 50000; i++)
        {
            ulong m = Next(ref state, i), c = Next(ref state, i + 1);
            a[0] = Next(ref state, i + 2); a[1] = Next(ref state, i + 3);
            t[0] = Next(ref state, i + 4); t[1] = Next(ref state, i + 5);
            UInt128 s = (UInt128)m * a[0] + t[0] + c;
            ulong e0 = (ulong)s;
            s = (UInt128)m * a[1] + t[1] + (ulong)(s >> 64);
            ulong e1 = (ulong)s, high = (ulong)(s >> 64);
            Check(RowDest(a, t, r, m, c), r, e0, e1, high);
            Check(RowDestParams(m, a[0], a[1], t[0], t[1], c, r), r, e0, e1, high);
            Check(RowDestReversed(m, a[0], a[1], t[0], t[1], c, r), r, e0, e1, high);
            Check(RowCarryFirst(m, a[0], a[1], t[0], t[1], c, r), r, e0, e1, high);
            r[2] = Next(ref state, i + 6);
            ulong redefined = r[2];
            Check(RowDestRedef(m, a[0], a[1], t[0], t[1], c, r), r, unchecked(e0 + redefined), e1, high);
            t[0] = 7; t[1] = 11; a[0] = 3; a[1] = 5;
            Check(RowDest(a, t, t, 2, 1), t, 14, 21, 0);
            a[0] = 3; a[1] = 5;
            Check(RowDest(a, t, a, 2, 1), a, 21, 31, 0);
            a[2] = Next(ref state, i + 2); t[2] = Next(ref state, i + 3);
            for (int j = 0; j < 5; j++) expected[j] = r[j] = Next(ref state, i + j);
            ulong expectedCarry = c;
            for (int j = 0; j < 3; j++)
            {
                UInt128 step = (UInt128)m * a[j] + t[j] + expectedCarry;
                expected[j] = unchecked((ulong)step + expected[j + 1]);
                expectedCarry = (ulong)(step >> 64);
            }
            if (LoopDest(a, t, r, m, c, 3) != expectedCarry) throw new Exception("Counted row carry");
            for (int j = 0; j < 5; j++)
                if (r[j] != expected[j]) throw new Exception("Counted row destination redefinition");
        }
        return 100;
    }

    private static void Check(ulong actual, ulong* r, ulong low, ulong middle, ulong high)
    {
        if (actual != high || r[0] != low || r[1] != middle)
            throw new Exception("Destination read crossed a local redefinition");
    }
}
