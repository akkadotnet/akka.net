﻿//-----------------------------------------------------------------------
// <copyright file="ThreadPoolBuilder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        /// TBD
        /// </summary>
        /// <param name="cfg">TBD</param>
        /// <returns>TBD</returns>
        internal static TimeSpan? GetSafeDeadlockTimeout(Config cfg)
        {
            var timespan = cfg.GetTimeSpan("deadlock-timeout", TimeSpan.FromSeconds(-1));
            if (timespan.TotalSeconds < 0)
                return null;
            return timespan;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="threadType">TBD</param>
        /// <returns>TBD</returns>
        internal static ThreadType ConfigureThreadType(string threadType)
        {
            return string.Compare(threadType, ThreadType.Foreground.ToString(), StringComparison.OrdinalIgnoreCase) == 0 ?
                ThreadType.Foreground : ThreadType.Background;
        }

#if UNSAFE_THREADING
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cfg">TBD</param>
        /// <returns>TBD</returns>
        internal static ApartmentState GetApartmentState(Config cfg)
        {
            var s = cfg.GetString("apartment");
            return string.Compare(s, "sta", StringComparison.OrdinalIgnoreCase) == 0
                ? ApartmentState.STA
                : string.Compare(s, "mta", StringComparison.OrdinalIgnoreCase) == 0
                    ? ApartmentState.MTA
                    : ApartmentState.Unknown;
        }
#endif

        /// <summary>
        /// Default settings for <see cref="SingleThreadDispatcher"/> instances.
        /// </summary>
        internal static readonly DedicatedThreadPoolSettings DefaultSingleThreadPoolSettings = new DedicatedThreadPoolSettings(1, "DefaultSingleThreadPool");
    }

    /// <summary>
    /// Used inside Akka.Remote for constructing the low-level Helios threadpool, but inside
    /// vanilla Akka it's also used for constructing custom fixed-size-threadpool dispatchers.
    /// </summary>
    public class ThreadPoolConfig
    {
        private readonly Config _config;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        public ThreadPoolConfig(Config config)
        {
            _config = config;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public int PoolSizeMin
        {
            get { return _config.GetInt("pool-size-min"); }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public double PoolSizeFactor
        {
            get { return _config.GetDouble("pool-size-factor"); }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public int PoolSizeMax
        {
            get { return _config.GetInt("pool-size-max"); }
        }

        #region Static methods

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="floor">TBD</param>
        /// <param name="scalar">TBD</param>
        /// <param name="ceiling">TBD</param>
        /// <returns>TBD</returns>
        public static int ScaledPoolSize(int floor, double scalar, int ceiling)
        {
            return Math.Min(Math.Max((int) (Environment.ProcessorCount*scalar), floor), ceiling);
        }

        #endregion
    }
}

