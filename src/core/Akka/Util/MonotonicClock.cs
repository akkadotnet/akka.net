//-----------------------------------------------------------------------
// <copyright file="MonotonicClock.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Akka.Util
{
	internal static class MonotonicClock
	{
		private static readonly Stopwatch Stopwatch = Stopwatch.StartNew();
        private static readonly bool IsMono = Type.GetType("Mono.Runtime") != null;

#if !DNXCORE50
        [DllImport("kernel32")]
		private static extern ulong GetTickCount64();
#endif

		private const int TicksInMillisecond = 10000;

        private const long NanosPerTick = 100;

		public static TimeSpan Elapsed
		{
			get
            {
				return TimeSpan.FromTicks(GetTicks());
			}
		}

		public static TimeSpan ElapsedHighRes
		{
			get { return Stopwatch.Elapsed; }
		}

	    public static long GetMilliseconds()
        {
#if !DNXCORE50
	        return IsMono
	            ? Stopwatch.ElapsedMilliseconds
	            : (long) GetTickCount64();
#else
            return Stopwatch.ElapsedMilliseconds;
#endif
        }

	    public static long GetNanos()
	    {
	        return GetTicks() * NanosPerTick;
	    }

	    public static long GetTicks()
	    {
	        return GetMilliseconds() * TicksInMillisecond;
	    }

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
