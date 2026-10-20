// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Xunit;

public static unsafe class Arm64MaskCarry
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Add(ulong a, ulong b, ref ulong c)
    {
        ulong s = a + b, k = s < a ? 1UL : 0UL, t = s + c;
        c = k + (t < s ? 1UL : 0UL);
        return t;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Sub(ulong a, ulong b, ref ulong c)
    {
        ulong s = a - b, k = a < b ? 1UL : 0UL, t = s - c;
        c = k + (s < c ? 1UL : 0UL);
        return t;
    }

    // Two distinct selects retain the mask local. The late mask pass must not
    // insert a TEST across a carry that an earlier pass has already forwarded.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong LowBit(ulong a, ulong b, ulong x, ulong y, ulong bit, ulong* r)
    {
        ulong c = 0;
        r[0] = Add(a, b, ref c);
        ulong mask = 0UL - (bit & 1);
        r[2] = x ^ ((x ^ y) & mask);
        r[3] = a ^ ((a ^ b) & mask);
        r[1] = Add(x, y, ref c);
        return c;
    }

    // The first TEST precedes the carry producer; the second select requires
    // a new TEST after it. Checking only the first insertion is insufficient.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong ReuseBit(ulong a, ulong b, ulong x, ulong y, ulong bit, ulong* r)
    {
        ulong mask = 0UL - (bit & 1);
        r[2] = x ^ ((x ^ y) & mask);
        ulong c = 0;
        r[0] = Add(a, b, ref c);
        r[3] = a ^ ((a ^ b) & mask);
        r[1] = Add(x, y, ref c);
        return c;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong Borrow(ulong a, ulong b, ulong x, ulong y, ulong bit, ulong* r)
    {
        ulong c = 0;
        r[0] = Sub(a, b, ref c);
        ulong mask = 0UL - (bit & 1);
        r[2] = x ^ ((x ^ y) & mask);
        r[3] = a ^ ((a ^ b) & mask);
        r[1] = Sub(x, y, ref c);
        return c;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong ReuseBorrow(ulong a, ulong b, ulong x, ulong y, ulong bit, ulong* r)
    {
        ulong mask = 0UL - (bit & 1);
        r[2] = x ^ ((x ^ y) & mask);
        ulong c = 0;
        r[0] = Sub(a, b, ref c);
        r[3] = a ^ ((a ^ b) & mask);
        r[1] = Sub(x, y, ref c);
        return c;
    }

    // Shared XOR deltas exercise the separate swap matcher.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong Shared(ulong a, ulong b, ulong x, ulong y, ulong bit, ulong* r)
    {
        ulong c = 0;
        r[0] = Add(a, b, ref c);
        ulong mask = 0UL - (bit & 1), d = (x ^ y) & mask, e = (a ^ b) & mask;
        r[2] = (x ^ d) + (a ^ e);
        r[3] = (y ^ d) + (b ^ e);
        r[1] = Add(x, y, ref c);
        return c;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MulAdd(ulong a, ulong b, ulong t, ref ulong c)
    {
        ulong hi = Math.BigMul(a, b, out ulong lo);
        ulong s = lo + t;
        hi += s < lo ? 1UL : 0UL;
        ulong result = s + c;
        c = hi + (result < s ? 1UL : 0UL);
        return result;
    }

    // Include a multiply/carry row on either side of the low-bit selects.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong Products(ulong a, ulong b, ulong x, ulong y, ulong bit, ulong* r)
    {
        ulong c = 0;
        r[0] = MulAdd(a, b, x, ref c);
        ulong mask = 0UL - (bit & 1);
        r[2] = x ^ ((x ^ y) & mask);
        r[3] = a ^ ((a ^ b) & mask);
        r[1] = MulAdd(a, y, b, ref c);
        return c;
    }

    [Fact]
    public static int TestEntryPoint()
    {
        ulong[] values = { 0, 1, 2, 4, 5, 0x80000000, 0xffffffff, 1UL << 32, 1UL << 63, ulong.MaxValue };
        ulong* r = stackalloc ulong[4];
        BigInteger wordMask = (BigInteger.One << 64) - 1;
        for (int repeat = 0; repeat < 100; repeat++)
        foreach (ulong a in values)
        foreach (ulong b in values)
        foreach (ulong bit in values)
        {
            ulong x = values[(repeat + 3) % values.Length], y = values[(repeat + 4) % values.Length];
            bool odd = (bit & 1) != 0;
            BigInteger sum = a + (BigInteger)b + ((x + (BigInteger)y) << 64);
            ulong carry = LowBit(a, b, x, y, bit, r);
            Check(sum, carry, r, wordMask);
            CheckSelect(a, b, x, y, odd, r);
            carry = ReuseBit(a, b, x, y, bit, r);
            Check(sum, carry, r, wordMask);
            CheckSelect(a, b, x, y, odd, r);
            BigInteger difference = a - (BigInteger)b + ((x - (BigInteger)y) << 64);
            carry = Borrow(a, b, x, y, bit, r);
            Check(difference, carry, r, wordMask);
            CheckSelect(a, b, x, y, odd, r);
            carry = ReuseBorrow(a, b, x, y, bit, r);
            Check(difference, carry, r, wordMask);
            CheckSelect(a, b, x, y, odd, r);
            carry = Shared(a, b, x, y, bit, r);
            Check(sum, carry, r, wordMask);
            if (r[2] != unchecked(odd ? y + b : x + a) || r[3] != unchecked(odd ? x + a : y + b))
                throw new Exception("Shared mask results");
            BigInteger product = (BigInteger)a * b + x + (((BigInteger)a * y + b) << 64);
            carry = Products(a, b, x, y, bit, r);
            Check(product, carry, r, wordMask);
            CheckSelect(a, b, x, y, odd, r);
        }
        return 100;
    }

    private static void Check(BigInteger expected, ulong carry, ulong* r, BigInteger wordMask)
    {
        ulong high = expected.Sign < 0 ? 1UL : (ulong)(expected >> 128);
        if (r[0] != (ulong)(expected & wordMask) || r[1] != (ulong)((expected >> 64) & wordMask) || carry != high)
            throw new Exception("Flags overwritten by mask select");
    }

    private static void CheckSelect(ulong a, ulong b, ulong x, ulong y, bool odd, ulong* r)
    {
        if (r[2] != (odd ? y : x) || r[3] != (odd ? b : a))
            throw new Exception("Low-bit mask results");
    }
}
