//-----------------------------------------------------------------------
// <copyright file="NetStandardCompat.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Akka.Reminders;

/// <summary>
/// Shims for APIs that exist on modern .NET but not on netstandard2.0, so the
/// reminders module can ship a single source tree to both targets.
/// </summary>
internal static class NetStandardCompat
{
    /// <summary>
    /// Multiplies a <see cref="TimeSpan"/> by an integer factor.
    /// </summary>
    /// <remarks>
    /// <c>TimeSpan * int</c> is a .NET 7+ operator. This is the equivalent
    /// tick arithmetic, kept in one place so the call sites read the same on
    /// both targets.
    /// </remarks>
    public static TimeSpan Double(this TimeSpan value) => TimeSpan.FromTicks(value.Ticks * 2);
}

#if !NET5_0_OR_GREATER
/// <summary>
/// Reference-equality comparer for <see cref="HashSet{T}"/> and dictionary keys.
/// </summary>
/// <remarks>
/// <c>System.Collections.Generic.ReferenceEqualityComparer</c> is .NET 5+. This
/// mirrors its behavior: identity comparison, ignoring any overridden
/// <see cref="object.Equals(object)"/> or <see cref="object.GetHashCode"/>.
/// </remarks>
internal sealed class ReferenceEqualityComparer : IEqualityComparer<object>
{
    public static readonly ReferenceEqualityComparer Instance = new();

    private ReferenceEqualityComparer() { }

    public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

    public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
}
#endif
