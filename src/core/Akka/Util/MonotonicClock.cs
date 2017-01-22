//-----------------------------------------------------------------------
// <copyright file="MonotonicClock.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Akka.Util
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    /// <param name="value">TBD</param>
    /// <returns>TBD</returns>
    internal static class MonotonicClock
    {
        private static readonly Stopwatch Stopwatch = Stopwatch.StartNew();

        [DllImport("kernel32")]
        private static extern ulong GetTickCount64();

        private const int TicksInMillisecond = 10000;

        private const long NanosPerTick = 100;

        /// <summary>
        /// TBD
        /// </summary>
        public static TimeSpan Elapsed
        {
            get
            {
                return TimeSpan.FromTicks(GetTicks());
            }
        }

        /// <summary>
        /// TBD
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
            return RuntimeDetector.IsMono
                ? Stopwatch.ElapsedMilliseconds
                : (long)GetTickCount64();
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
