//-----------------------------------------------------------------------
// <copyright file="ThreadPoolBuilder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Configuration;
using Helios.Concurrency;

namespace Akka.Dispatch
{
    /// <summary>
    /// <see cref="Config"/> helper class for configuring <see cref="MessageDispatcherConfigurator"/>
    /// instances who depend on the Helios <see cref="DedicatedThreadPool"/>.
    /// </summary>
    internal static class DedicatedThreadPoolConfigHelpers
    {
        /// <summary>
        /// Reads the configured deadlock-detection timeout, treating negative values as disabled.
        /// </summary>
        /// <param name="cfg">The configuration containing the <c>deadlock-timeout</c> setting.</param>
        /// <returns>The configured timeout, or <c>null</c> when the setting is negative.</returns>
        internal static TimeSpan? GetSafeDeadlockTimeout(Config cfg)
        {
            var timespan = cfg.GetTimeSpan("deadlock-timeout", TimeSpan.FromSeconds(-1));
            if (timespan.TotalSeconds < 0)
                return null;
            return timespan;
        }

        /// <summary>
        /// Parses a thread type, defaulting to background unless the value names the foreground type.
        /// </summary>
        /// <param name="threadType">The configured thread type name.</param>
        /// <returns><see cref="ThreadType.Foreground"/> for a case-insensitive "Foreground" value; otherwise, <see cref="ThreadType.Background"/>.</returns>
        internal static ThreadType ConfigureThreadType(string threadType)
        {
            return string.Compare(threadType, ThreadType.Foreground.ToString(), StringComparison.OrdinalIgnoreCase) == 0 ?
                ThreadType.Foreground : ThreadType.Background;
        }

        /// <summary>
        /// Default settings for SingleThreadDispatcher instances.
        /// </summary>
        internal static readonly DedicatedThreadPoolSettings DefaultSingleThreadPoolSettings = new(1, "DefaultSingleThreadPool");
    }

    /// <summary>
    /// Used inside Akka.Remote for constructing the low-level Helios threadpool, but inside
    /// vanilla Akka it's also used for constructing custom fixed-size-threadpool dispatchers.
    /// </summary>
    public class ThreadPoolConfig
    {
        private readonly Config _config;

        /// <summary>
        /// Creates a thread-pool configuration reader for the supplied settings.
        /// </summary>
        /// <param name="config">The configuration containing pool-size settings.</param>
        public ThreadPoolConfig(Config config)
        {
            _config = config;
        }

        /// <summary>
        /// Gets the configured minimum thread-pool size, or zero when the setting is absent.
        /// </summary>
        public int PoolSizeMin
        {
            get { return _config.GetInt("pool-size-min", 0); }
        }

        /// <summary>
        /// Gets the configured processor-count multiplier for calculating the pool size, or zero when absent.
        /// </summary>
        public double PoolSizeFactor
        {
            get { return _config.GetDouble("pool-size-factor", 0); }
        }

        /// <summary>
        /// Gets the configured maximum thread-pool size, or zero when the setting is absent.
        /// </summary>
        public int PoolSizeMax
        {
            get { return _config.GetInt("pool-size-max", 0); }
        }

        #region Static methods

        /// <summary>
        /// Calculates a processor-based pool size and clamps it to the supplied bounds.
        /// </summary>
        /// <param name="floor">The lower bound for the result.</param>
        /// <param name="scalar">The multiplier applied to <see cref="Environment.ProcessorCount"/> before conversion to an integer.</param>
        /// <param name="ceiling">The upper bound for the result.</param>
        /// <returns>The processor count multiplied by <paramref name="scalar"/>, converted to an integer and clamped between <paramref name="floor"/> and <paramref name="ceiling"/>.</returns>
        public static int ScaledPoolSize(int floor, double scalar, int ceiling)
        {
            return Math.Min(Math.Max((int) (Environment.ProcessorCount*scalar), floor), ceiling);
        }

        #endregion
    }
}
