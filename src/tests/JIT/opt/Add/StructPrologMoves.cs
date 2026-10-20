// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using Xunit;

// Hidden struct-return buffers and struct parameters can create register
// cycles in the prolog under stress. Peepholes must inspect that prolog's
// instructions, rather than the already-emitted end of the method body.
public static class StructPrologMoves
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static UInt128 Increment(UInt128 value) => value + 1;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static UInt128 AddAndCopy(UInt128 value, UInt128 other, out UInt128 copy)
    {
        copy = value;
        return value + other + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static UInt128 SwapAndIncrement(UInt128 value, ref UInt128 other)
    {
        UInt128 saved = other;
        other = value;
        return saved + 1;
    }

    [Fact]
    public static int TestEntryPoint()
    {
        ulong[] words = { 0, 1, 2, ulong.MaxValue, ulong.MaxValue - 1, 1UL << 63, 1UL << 32 };
        foreach (ulong lo in words)
        foreach (ulong hi in words)
        foreach (ulong second in words)
        {
            UInt128 value = ((UInt128)hi << 64) | lo;
            UInt128 other = ((UInt128)second << 64) | hi;
            UInt128 expectedIncrement = ReferenceAdd(value, 0);
            if (Increment(value) != expectedIncrement) throw new Exception("Struct prolog increment");
            if (AddAndCopy(value, other, out UInt128 copy) != ReferenceAdd(value, other) || copy != value)
                throw new Exception("Struct prolog return/copy buffers");
            UInt128 saved = other;
            if (SwapAndIncrement(value, ref other) != ReferenceAdd(saved, 0) || other != value)
                throw new Exception("Struct prolog aliasable buffer");
        }
        return 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static UInt128 ReferenceAdd(UInt128 a, UInt128 b)
    {
        UInt128 low = (UInt128)(ulong)a + (ulong)b + 1;
        ulong high = (ulong)(a >> 64) + (ulong)(b >> 64) + (ulong)(low >> 64);
        return ((UInt128)high << 64) | (ulong)low;
    }
}
