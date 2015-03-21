using System;
using System.Diagnostics;

namespace Akka.Remote
{
    /// <summary>
    /// INTERNAL API - used to get precise elapsed time.
    /// </summary>
    internal static class SystemNanoTime
    {
        /// <summary>
        /// Need to have time that is much more precise than <see cref="DateTime.Now"/> when throttling sends
        /// </summary>
        private static readonly Stopwatch StopWatch;

        static SystemNanoTime()
        {
            StopWatch = new Stopwatch();
            StopWatch.Start();
        }

        public static long GetNanos()
        {
            return StopWatch.ElapsedTicks.ToNanos();
        }

        internal const long NanosPerTick = 100;

        /// <summary>
        /// Ticks represent 100 nanos. https://msdn.microsoft.com/en-us/library/system.datetime.ticks(v=vs.110).aspx
        /// 
        /// This extension method converts a Ticks value to nano seconds.
        /// </summary>
        internal static long ToNanos(this long ticks)
        {
            return ticks*NanosPerTick;
        }

        /// <summary>
        /// Ticks represent 100 nanos. https://msdn.microsoft.com/en-us/library/system.datetime.ticks(v=vs.110).aspx
        /// 
        /// This extension method converts a nano seconds value to Ticks.
        /// </summary>
        internal static long ToTicks(this long nanos)
        {
            return nanos/NanosPerTick;
        }
    }
}