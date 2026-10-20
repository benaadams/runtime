// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Xunit;

public static class Arm64UnrolledCarry
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Add(ulong a, ulong b, ref ulong carry)
    {
        ulong s = a + b, c = s < a ? 1UL : 0UL;
        ulong t = s + carry;
        carry = c + (t < s ? 1UL : 0UL);
        return t;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong High(ulong a, ulong b) => Math.BigMul(a, b, out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Row(ulong m, ulong a0, ulong a1, ulong a2, ulong a3, ulong a4, ulong a5,
        ref ulong t0, ref ulong t1, ref ulong t2, ref ulong t3, ref ulong t4, ref ulong t5)
    {
        ulong c = 0;
        t0 = Add(t0, m * a0, ref c); t1 = Add(t1, m * a1, ref c);
        t2 = Add(t2, m * a2, ref c); t3 = Add(t3, m * a3, ref c);
        t4 = Add(t4, m * a4, ref c); t5 = Add(t5, m * a5, ref c);
        ulong top = c;
        c = 0;
        t1 = Add(t1, High(m, a0), ref c); t2 = Add(t2, High(m, a1), ref c);
        t3 = Add(t3, High(m, a2), ref c); t4 = Add(t4, High(m, a3), ref c);
        t5 = Add(t5, High(m, a4), ref c);
        return top + High(m, a5) + c;
    }

    // Twelve whole-vector chains and widening products in one block. Each
    // removed carry temporary must be proved single-use through the rest of
    // the block, including the final rows beyond the old scan allowance.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong Kernel(ulong[] a, ulong[] t, ulong m)
    {
        ulong a0 = a[0], a1 = a[1], a2 = a[2], a3 = a[3], a4 = a[4], a5 = a[5];
        ulong t0 = t[0], t1 = t[1], t2 = t[2], t3 = t[3], t4 = t[4], t5 = t[5];
        ulong top = Row(m, a0,a1,a2,a3,a4,a5, ref t0,ref t1,ref t2,ref t3,ref t4,ref t5);
        top ^= Row(~m, a0,a1,a2,a3,a4,a5, ref t0,ref t1,ref t2,ref t3,ref t4,ref t5);
        top ^= Row(m+1, a0,a1,a2,a3,a4,a5, ref t0,ref t1,ref t2,ref t3,ref t4,ref t5);
        top ^= Row(m^0x8000000000000000UL, a0,a1,a2,a3,a4,a5, ref t0,ref t1,ref t2,ref t3,ref t4,ref t5);
        top ^= Row(m>>1, a0,a1,a2,a3,a4,a5, ref t0,ref t1,ref t2,ref t3,ref t4,ref t5);
        top ^= Row(m, a0,a1,a2,a3,a4,a5, ref t0,ref t1,ref t2,ref t3,ref t4,ref t5);
        t[0]=t0; t[1]=t1; t[2]=t2; t[3]=t3; t[4]=t4; t[5]=t5;
        return top;
    }

    [Fact]
    public static int TestEntryPoint()
    {
        var random = new Random(0x41524d36);
        ulong Next() => random.Next(4) switch
        {
            0 => 0, 1 => ulong.MaxValue,
            _ => ((ulong)random.NextInt64() << 1) | (uint)random.Next(2)
        };
        BigInteger radix = BigInteger.One << 384;
        for (int iteration = 0; iteration < 4000; iteration++)
        {
            ulong[] a = {Next(),Next(),Next(),Next(),Next(),Next()};
            ulong[] t = {Next(),Next(),Next(),Next(),Next(),Next()};
            ulong m = Next();
            BigInteger aa = 0, tt = 0;
            for (int j=5;j>=0;j--) { aa = (aa << 64) + a[j]; tt = (tt << 64) + t[j]; }
            ulong expectedTop = 0;
            foreach (ulong factor in new[] {m,~m,unchecked(m+1),m^0x8000000000000000UL,m>>1,m})
            {
                BigInteger sum = aa * factor + tt;
                expectedTop ^= (ulong)(sum >> 384);
                tt = sum % radix;
            }
            if (Kernel(a,t,m) != expectedTop) throw new Exception("Unrolled row carry mismatch");
            for (int j=0;j<6;j++)
                if (t[j] != (ulong)((tt >> (64*j)) & ulong.MaxValue))
                    throw new Exception("Unrolled row limb mismatch");
        }
        return 100;
    }
}
