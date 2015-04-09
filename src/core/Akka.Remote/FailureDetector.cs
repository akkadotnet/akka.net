//-----------------------------------------------------------------------
// <copyright file="FailureDetector.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

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

		//fixed: sign will no longer flip, but the tickcount will go back down to zero every 24.9 days 
        public static readonly Clock DefaultClock = () => Environment.TickCount & Int32.MaxValue;

        #endregion
    }

    /// <summary>
    /// Abstraction of a clock that returns time in milliseconds. Can only be used to measure the elapsed time
    /// and is not related to any other notion of system or wall-clock time.
    /// </summary>
    public delegate long Clock();

}

