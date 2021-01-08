//-----------------------------------------------------------------------
// <copyright file="FailureDetector.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Util;

namespace Akka.Remote
{
    /// <summary>
    /// A failure detector must be a thread-safe, mutable construct that registers heartbeat events of a resource and
    /// is able to decide the availability of that monitored resource
    /// </summary>
    public abstract class FailureDetector
    {
        /// <summary>
        /// Returns true if the resource is considered to be up and healthy; false otherwise
        /// </summary>
        public abstract bool IsAvailable { get; }

        /// <summary>
        /// Returns true if the failure detector has received any heartbeats and started monitoring
        /// the resource
        /// </summary>
        public abstract bool IsMonitoring { get; }

        /// <summary>
        /// Notifies the <see cref="FailureDetector"/> that a heartbeat arrived from the monitored resource.
        /// This causes the <see cref="FailureDetector"/> to update its state.
        /// </summary>
        public abstract void HeartBeat();

        #region Static members

        /// <summary>
        /// The default clock implementation used by the <see cref="PhiAccrualFailureDetector"/>
        /// </summary>
        /// <returns>A clock instance.</returns>
        public static readonly Clock DefaultClock = () => MonotonicClock.GetMilliseconds();

        #endregion
    }

    /// <summary>
    /// Abstraction of a clock that returns time in milliseconds. Can only be used to measure the elapsed time
    /// and is not related to any other notion of system or wall-clock time.
    /// </summary>
    public delegate long Clock();

}

