// -----------------------------------------------------------------------
//  <copyright file="Extensions.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Data;
using System.Runtime.CompilerServices;
using Akka.Hosting;

namespace Akka.Persistence.Hosting
{
    public static class Extensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToHocon(this IsolationLevel? level)
        {
            if (level is null)
                throw new ArgumentNullException(nameof(level));
            return level.Value.ToHocon();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToHocon(this IsolationLevel level)
        {
            return level switch
            {
                IsolationLevel.Unspecified => "unspecified".ToHocon(),
                IsolationLevel.ReadCommitted => "read-committed".ToHocon(),
                IsolationLevel.ReadUncommitted => "read-uncommitted".ToHocon(),
                IsolationLevel.RepeatableRead => "repeatable-read".ToHocon(),
                IsolationLevel.Serializable => "serializable".ToHocon(),
                IsolationLevel.Snapshot => "snapshot".ToHocon(),
                IsolationLevel.Chaos => "chaos".ToHocon(),
                _ => throw new IndexOutOfRangeException($"Unknown IsolationLevel value: {level}"),
            };
        }

    }
}
