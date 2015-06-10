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

		public static TimeSpan Elapsed
		{
			get
            {
#if DNXCORE50
                return Stopwatch.Elapsed;
#else
                return IsMono
					? Stopwatch.Elapsed
					: new TimeSpan((long) GetTickCount64()*TicksInMillisecond);
#endif
            }
        }

		public static TimeSpan ElapsedHighRes
		{
			get { return Stopwatch.Elapsed; }
		}
	}
}
