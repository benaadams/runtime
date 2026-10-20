// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using Xunit;

public static class Arm64FlagPreservation
{
    private struct Words { public ulong X, Y, Z; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Expose(ref Words words) { words.Y ^= words.X; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong StackCarry(ulong x, ulong y, ulong z, out ulong middle, out ulong top)
    {
        Words words = new Words { X=x, Y=y, Z=z };
        Expose(ref words);
        ulong c = 0;
        ulong low = Add(x, y, ref c);
        words.Z = words.X ^ low;
        middle = Add(words.Y, z, ref c);
        top = Add(words.Z, x, ref c);
        return c;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Add(ulong a, ulong b, ref ulong carry)
    {
        ulong s = a + b;
        ulong c = s < a ? 1UL : 0UL;
        ulong t = s + carry;
        carry = c + (t < s ? 1UL : 0UL);
        return t;
    }

    // Each carry crosses a load, a store, a complement and a widening product.
    // The store can alias a later input: forwarding flags must not move memory.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong Chain(ulong[] a, ulong[] b, ulong[] r, ulong m)
    {
        ulong c = 0;
        r[0] = Add(a[0], b[0], ref c);
        ulong hi = Math.BigMul(m, a[1], out ulong lo);
        r[1] = Add(a[1], ~b[1], ref c);
        r[2] = Add(a[2], b[2], ref c);
        r[3] = Add(lo, hi, ref c);
        return c;
    }

    // A comparison mask has several consumers separated by flag-preserving
    // arithmetic. The low-bit mask needs a new TEST after the clobbering compare.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong Selects(ulong a, ulong b, ulong bit, ulong[] r)
    {
        ulong mask = 0UL - (a < b ? 1UL : 0UL);
        r[0] = a ^ ((a ^ b) & mask);
        ulong h = Math.BigMul(a, b, out ulong l);
        r[1] = h ^ ((h ^ l) & mask);
        ulong odd = 0UL - (bit & 1);
        ulong x = a ^ ((a ^ b) & odd);
        r[2] = x;
        ulong compare = x < h ? 1UL : 0UL;
        return (h ^ ((h ^ l) & odd)) + compare;
    }

    // These accesses must remain observable, including with aliased arguments.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong VolatileCarry(ref ulong a, ref ulong b)
    {
        ulong x = Volatile.Read(ref a), y = Volatile.Read(ref b);
        ulong s = x + y, c = s < x ? 1UL : 0UL;
        Volatile.Write(ref a, s);
        return Volatile.Read(ref b) + c;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong ZeroSelect(ulong x, ulong a, ulong b, out ulong other)
    {
        ulong mask = 0UL - (x == 0 ? 1UL : 0UL);
        ulong first = b ^ ((a ^ b) & mask);
        other = x ^ ((~a ^ x) & mask);
        return first;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MulAdd(ulong m, ulong x, ulong t, ulong c, out ulong low)
    {
        ulong high = Math.BigMul(m, x, out ulong l);
        l += t;
        high += l < t ? 1UL : 0UL;
        low = l + c;
        return high + (low < c ? 1UL : 0UL);
    }

    // An arbitrary full-word incoming carry, multiple rows and unrelated live
    // values. Retaining the first row's final carry must still drain NZCV.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong Rows(ulong[] a, ulong[] t, ulong m, ulong incoming, ulong[] r, out ulong first)
    {
        ulong a0 = a[0], a1 = a[1], a2 = a[2];
        ulong t0 = t[0], t1 = t[1], t2 = t[2];
        ulong c = MulAdd(m, a0, t0, incoming, out t0);
        c = MulAdd(m, a1, t1, c, out t1);
        c = MulAdd(m, a2, t2, c, out t2);
        first = c;
        c = MulAdd(m, a2, t0, incoming, out t0);
        c = MulAdd(m, a1, t1, c, out t1);
        c = MulAdd(m, a0, t2, c, out t2);
        r[0] = t0; r[1] = t1; r[2] = t2;
        return c;
    }

    [Fact]
    public static int TestEntryPoint()
    {
        var random = new Random(0x4e5a4356);
        ulong Next() => random.Next(4) switch
        {
            0 => 0, 1 => ulong.MaxValue,
            _ => ((ulong)random.NextInt64() << 1) | (uint)random.Next(2)
        };
        for (int iteration = 0; iteration < 20000; iteration++)
        {
            ulong[] a = { Next(), Next(), Next(), Next() };
            ulong[] b = { Next(), Next(), Next(), Next() };
            ulong m = Next();
            ulong incoming = Next();
            BigInteger radix = BigInteger.One << 64;
            BigInteger input = a[0] + radix * a[1] + radix * radix * a[2];
            BigInteger accumulator = b[0] + radix * b[1] + radix * radix * b[2];
            BigInteger row = input * m + accumulator + incoming;
            BigInteger reverse = a[2] + radix * a[1] + radix * radix * a[0];
            BigInteger expectedRow = reverse * m + row % (radix * radix * radix) + incoming;
            ulong[] rowOutput = new ulong[3];
            ulong final = Rows(a, b, m, incoming, rowOutput, out ulong firstCarry);
            if (firstCarry != (ulong)(row >> 192) || final != (ulong)(expectedRow >> 192) ||
                rowOutput[0] != (ulong)(expectedRow & ulong.MaxValue) ||
                rowOutput[1] != (ulong)((expectedRow >> 64) & ulong.MaxValue) ||
                rowOutput[2] != (ulong)((expectedRow >> 128) & ulong.MaxValue))
                throw new Exception("Multiply row mismatch");
            for (int alias = 0; alias < 3; alias++)
            {
                ulong[] x = (ulong[])a.Clone(), y = (ulong[])b.Clone();
                ulong[] r = alias == 0 ? new ulong[4] : alias == 1 ? x : y;
                BigInteger word = BigInteger.One << 64;
                BigInteger p = (BigInteger)m * a[1];
                BigInteger s0 = (BigInteger)a[0] + b[0];
                BigInteger s1 = (BigInteger)a[1] + ~b[1] + (s0 >> 64);
                BigInteger s2 = (BigInteger)a[2] + b[2] + (s1 >> 64);
                BigInteger s3 = (p % word) + (p >> 64) + (s2 >> 64);
                ulong c = Chain(x, y, r, m);
                if (r[0] != (ulong)(s0 % word) || r[1] != (ulong)(s1 % word) ||
                    r[2] != (ulong)(s2 % word) || r[3] != (ulong)(s3 % word) || c != (ulong)(s3 >> 64))
                    throw new Exception("Carry across memory/product mismatch");
            }
            ulong u = Next(), v = Next(), bit = Next();
            BigInteger stack0 = (BigInteger)u + v;
            BigInteger stack1 = (BigInteger)(u ^ v) + bit + (stack0 >> 64);
            BigInteger stack2 = (BigInteger)(u ^ (ulong)(stack0 & ulong.MaxValue)) + u + (stack1 >> 64);
            ulong stackCarry = StackCarry(u, v, bit, out ulong stackMiddle, out ulong stackTop);
            if (stackCarry != (ulong)(stack2 >> 64) || stackMiddle != (ulong)(stack1 & ulong.MaxValue) ||
                stackTop != (ulong)(stack2 & ulong.MaxValue))
                throw new Exception("Carry across stack fields mismatch");
            ulong zeroResult = ZeroSelect(bit, u, v, out ulong zeroOther);
            if (zeroResult != (bit == 0 ? u : v) || zeroOther != (bit == 0 ? ~u : bit))
                throw new Exception("Comparison-mask select mismatch");
            ulong[] outputs = new ulong[3];
            BigInteger product = (BigInteger)u * v;
            ulong low = (ulong)(product & ulong.MaxValue), high = (ulong)(product >> 64);
            ulong first = u < v ? v : u, selected = (bit & 1) != 0 ? v : u;
            ulong last = unchecked(((bit & 1) != 0 ? low : high) + (selected < high ? 1UL : 0UL));
            if (Selects(u, v, bit, outputs) != last || outputs[0] != first ||
                outputs[1] != (u < v ? low : high) || outputs[2] != selected)
                throw new Exception("Flag select mismatch");

            ulong z = u, expected = unchecked(u + v);
            if (VolatileCarry(ref z, ref v) != unchecked(v + (expected < u ? 1UL : 0UL)) || z != expected)
                throw new Exception("Volatile carry mismatch");
            z = u; expected = unchecked(u + u);
            if (VolatileCarry(ref z, ref z) != unchecked(expected + (expected < u ? 1UL : 0UL)) || z != expected)
                throw new Exception("Aliased volatile carry mismatch");
        }
        return 100;
    }
}
