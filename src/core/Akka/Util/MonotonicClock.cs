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
		[DllImport("kernel32")]
		private static extern ulong GetTickCount64();

		private const int TicksInMillisecond = 10000;

		public static TimeSpan Elapsed
		{
			get
			{
				return IsMono
					? Stopwatch.Elapsed
					: new TimeSpan((long) GetTickCount64()*TicksInMillisecond);
			}
		}

		public static TimeSpan ElapsedHighRes
		{
			get { return Stopwatch.Elapsed; }
		}
	}
}
