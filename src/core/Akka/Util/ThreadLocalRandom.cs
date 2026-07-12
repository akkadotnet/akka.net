//-----------------------------------------------------------------------
// <copyright file="ThreadLocalRandom.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Security.Cryptography;
using System.Threading;

namespace Akka.Util
{
    /// <summary>
    /// Create random numbers with Thread-specific seeds.
    ///
    /// Borrowed form Jon Skeet's brilliant C# in Depth: http://csharpindepth.com/Articles/Chapter12/Random.aspx
    ///
    /// The per-thread seed base is drawn from a cryptographically random source rather than
    /// <see cref="Environment.TickCount"/>. Processes launched at nearly the same moment (e.g.
    /// orchestrator-started cluster nodes, or the node processes spun up by the multi-node test
    /// runner) previously shared the same <see cref="Environment.TickCount"/>-derived base, which
    /// meant corresponding threads across those processes drew identical random streams from
    /// <see cref="Current"/>. A cryptographically random base removes that deterministic
    /// cross-process correlation.
    /// </summary>
    public static class ThreadLocalRandom
    {
        private static int _seed = CreateSeed();

        private static readonly ThreadLocal<Random> _rng = new(() => new Random(Interlocked.Increment(ref _seed)));

        /// <summary>
        /// Draws the per-process seed base from the operating system's cryptographic random source.
        /// Kept as a separate method so the entropy source can be covered without depending on
        /// thread scheduling or comparing two already-distinct per-thread increments.
        /// </summary>
        internal static int CreateSeed() => RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue);

        /// <summary>
        /// The current random number seed available to this thread
        /// </summary>
        public static Random Current
        {
            get
            {
                return _rng.Value;
            }
        }
    }
}
