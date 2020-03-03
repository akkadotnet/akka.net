//-----------------------------------------------------------------------
// <copyright file="MonotonicClock.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Akka.Util
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// A Monotonic clock implementation based on total uptime.
    /// Used for keeping accurate time internally.
    /// </summary>
    internal static class MonotonicClock
    {
        private static readonly Stopwatch Stopwatch = Stopwatch.StartNew();

        private const int TicksInMillisecond = 10000;

        private const long NanosPerTick = 100;

        /// <summary>
        /// Time as measured by the current system up-time.
        /// </summary>
        public static TimeSpan Elapsed
        {
            get
            {
                return TimeSpan.FromTicks(GetTicks());
            }
        }

        /// <summary>
        /// High resolution elapsed time as determined by a <see cref="Stopwatch"/>
        /// running continuously in the background.
        /// </summary>
        public static TimeSpan ElapsedHighRes
        {
            get { return Stopwatch.Elapsed; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long GetMilliseconds()
        {
            return Stopwatch.ElapsedMilliseconds;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long GetNanos()
        {
            return GetTicks() * NanosPerTick;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long GetTicks()
        {
            return GetMilliseconds() * TicksInMillisecond;
        }

        /// <summary>
        /// Ticks represent 100 nanos. https://msdn.microsoft.com/en-us/library/system.datetime.ticks(v=vs.110).aspx
        /// 
        /// This extension method converts a Ticks value to nano seconds.
        /// </summary>
        /// <param name="ticks">TBD</param>
        /// <returns>TBD</returns>
        internal static long ToNanos(this long ticks)
        {
            return ticks*NanosPerTick;
        }

        /// <summary>
        /// Ticks represent 100 nanos. https://msdn.microsoft.com/en-us/library/system.datetime.ticks(v=vs.110).aspx
        /// 
        /// This extension method converts a nano seconds value to Ticks.
        /// </summary>
        /// <param name="nanos">TBD</param>
        /// <returns>TBD</returns>
        internal static long ToTicks(this long nanos)
        {
            return nanos/NanosPerTick;
        }
    }
}
