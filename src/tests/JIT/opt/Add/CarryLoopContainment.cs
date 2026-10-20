// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using Xunit;

// TryLowerCarryLoop rechecks containment after forwarding its last load.
// JitStress=1/REVERSE_FLAG/SPLIT_TREES_REMOVE_COMMAS can leave the other
// operand reg-optional from the first check. Retain both span-loop shapes.
public static class CarryLoopContainment
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint Add(nuint a, nuint b, nuint c, out nuint carry)
    {
        if (IntPtr.Size == 8)
        {
            nuint sum = a + b;
            nuint first = sum < a ? 1 : (nuint)0;
            nuint result = sum + c;
            nuint second = result < sum ? 1 : (nuint)0;
            carry = first + second;
            return result;
        }
        else
        {
            ulong sum = (ulong)a + b + c;
            carry = (uint)(sum >> 32);
            return (uint)sum;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SeedOne(ReadOnlySpan<nuint> a, ReadOnlySpan<nuint> b, Span<nuint> r)
    {
        _ = a[b.Length - 1]; _ = r[b.Length];
        nuint carry = 1;
        for (int i = 0; i < b.Length; i++) r[i] = Add(a[i], b[i], carry, out carry);
        r[b.Length] = carry;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DiscardCarry(ReadOnlySpan<nuint> a, ReadOnlySpan<nuint> b, Span<nuint> r)
    {
        _ = a[b.Length - 1]; _ = r[b.Length];
        nuint carry = 0;
        for (int i = 0; i < b.Length; i++) r[i] = Add(a[i], b[i], carry, out carry);
    }

    [Fact]
    public static int TestEntryPoint()
    {
        ulong state = 0x2537c011UL;
        foreach (int length in new[] { 1, 2, 3, 7, 16, 33 })
        for (int iteration = 0; iteration < 128; iteration++)
        {
            nuint[] a = new nuint[length + 1], b = new nuint[length], result = new nuint[length + 1];
            for (int j = 0; j < length; j++)
            {
                state ^= state << 13; state ^= state >> 7; state ^= state << 17;
                a[j] = iteration == 0 ? nuint.MaxValue : iteration == 1 ? 0 : (nuint)state;
                state ^= state << 13; state ^= state >> 7; state ^= state << 17;
                b[j] = iteration == 0 ? 0 : iteration == 1 ? nuint.MaxValue : (nuint)state;
            }
            SeedOne(a, b, result);
            Verify(a, b, result, 1, true);
            DiscardCarry(a, b, result);
            Verify(a, b, result, 0, false);
            nuint[] original = (nuint[])a.Clone();
            SeedOne(a, b, a);
            Verify(original, b, a, 1, true);
        }
        return 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static void Verify(nuint[] a, nuint[] b, nuint[] actual, ulong carry, bool checkCarry)
    {
        for (int j = 0; j < b.Length; j++)
        {
            UInt128 expected = (UInt128)(ulong)a[j] + (ulong)b[j] + carry;
            if (actual[j] != (nuint)(ulong)expected) throw new Exception("Carry loop limb");
            carry = (ulong)(expected >> (IntPtr.Size * 8));
        }
        if (checkCarry && actual[b.Length] != (nuint)carry) throw new Exception("Carry loop final carry");
    }
}
