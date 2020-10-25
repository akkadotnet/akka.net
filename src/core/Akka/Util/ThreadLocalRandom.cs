//-----------------------------------------------------------------------
// <copyright file="ThreadLocalRandom.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;

namespace Akka.Util
{
    /// <summary>
    /// Create random numbers with Thread-specific seeds.
    /// 
    /// Borrowed form Jon Skeet's brilliant C# in Depth: http://csharpindepth.com/Articles/Chapter12/Random.aspx
    /// </summary>
    public static class ThreadLocalRandom
    {
        private static int _seed = Environment.TickCount;

        [ThreadStatic] 
        private static Random _rng2;
        //private static ThreadLocal<Random> _rng = new ThreadLocal<Random>(() => new Random(Interlocked.Increment(ref _seed)));

        /// <summary>
        /// The current random number seed available to this thread
        /// </summary>
        public static Random Current
        {
            get
            {
                if (_rng2 == null)
                {
                    Interlocked.CompareExchange(ref _rng2,
                        new Random(Interlocked.Increment(ref _seed)), null);
                }

                return _rng2;
            }
        }
    }
}

