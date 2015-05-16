﻿//-----------------------------------------------------------------------
// <copyright file="ThreadPoolBuilder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
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
        internal static TimeSpan? GetSafeDeadlockTimeout(Config cfg)
        {
            var timespan = cfg.GetTimeSpan("deadlock-timeout", TimeSpan.FromSeconds(-1));
            if (timespan.TotalSeconds < 0)
                return null;
            return timespan;
        }

        internal static ThreadType ConfigureThreadType(string threadType)
        {
            return string.Compare(threadType, ThreadType.Foreground.ToString(), StringComparison.InvariantCultureIgnoreCase) == 0 ?
                ThreadType.Foreground : ThreadType.Background;
        }

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

        public ThreadPoolConfig(Config config)
        {
            _config = config;
        }

        public int PoolSizeMin
        {
            get { return _config.GetInt("pool-size-min"); }
        }

        public double PoolSizeFactor
        {
            get { return _config.GetDouble("pool-size-factor"); }
        }

        public int PoolSizeMax
        {
            get { return _config.GetInt("pool-size-max"); }
        }

        #region Static methods

        public static int ScaledPoolSize(int floor, double scalar, int ceiling)
        {
            return Math.Min(Math.Max((int) (Environment.ProcessorCount*scalar), floor), ceiling);
        }

        #endregion
    }
}

