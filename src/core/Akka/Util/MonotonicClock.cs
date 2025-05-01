//-----------------------------------------------------------------------
// <copyright file="MonotonicClock.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

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
        private static readonly Stopwatch Stopwatch;

        private const long TicksInMillisecond = TimeSpan.TicksPerMillisecond;
        private const long TicksInSecond = TimeSpan.TicksPerSecond;
        private const long NanosPerTick = 100;

        /// <summary>
        /// TickFrequency is a constant scaling value to normalize operating system reported performance counter
        /// clock tick to .NET internal definition of a "tick".
        /// </summary>
        private static readonly double TicksFrequency;
        
        static MonotonicClock()
        {
            // TickFrequency is a constant scaling value to normalize operating system reported performance counter
            // clock tick to .NET internal definition of a "tick".
            //
            // Stopwatch.ElapsedTicks returns the raw operating system level performance clock ticks, which is not
            // the same definition as .NET DateTime or TimeSpan definition.
            //
            // .NET DateTime or TimeSpan definition of a "tick" is 100 nanosecond (sampling frequency of 10 MHz)
            // which is true for all windows platforms that support performance counter clock (win8+); however
            // this is not true for linux platforms, their definition of a "tick" is 1 nanosecond (sampling
            // frequency of 1 GHz).
            TicksFrequency = (double)TicksInSecond / Stopwatch.Frequency;
            
            Stopwatch = Stopwatch.StartNew();
        }
        
        /// <summary>
        /// Time as measured by the current system up-time.
        /// </summary>
        public static TimeSpan Elapsed => TimeSpan.FromTicks(GetTicks());

        /// <summary>
        /// High resolution elapsed time as determined by a <see cref="Stopwatch"/>
        /// running continuously in the background.
        /// </summary>
        public static TimeSpan ElapsedHighRes => Stopwatch.Elapsed;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long GetTicksHighRes() => (long)(Stopwatch.ElapsedTicks * TicksFrequency);

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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long ToTicks(this long nanos)
        {
            return nanos/NanosPerTick;
        }

        internal static bool IsHighResolution => Stopwatch.IsHighResolution;
    }
}
