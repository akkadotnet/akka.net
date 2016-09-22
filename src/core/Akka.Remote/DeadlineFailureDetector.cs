//-----------------------------------------------------------------------
// <copyright file="DeadlineFailureDetector.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Configuration;
using Akka.Event;

namespace Akka.Remote
{
    /// <summary>
    /// Implementation of failure detector using an absolute timeout of missing heartbeats
    /// to trigger unavailability
    /// </summary>
    public class DeadlineFailureDetector : FailureDetector
    {
        private readonly Clock _clock;

        private long _heartbeatTimestamp = 0L; //not used until active (first heartbeat)
        private volatile bool _active = false;
        private readonly long _deadlineMillis;

        [Obsolete("Use DeadlineFailureDetector(acceptableHeartbeatPause, heartbeatInterval, clock) instead. (1.1.2)")]
        public DeadlineFailureDetector(TimeSpan acceptableHeartbeatPause, Clock clock = null) 
            : this(acceptableHeartbeatPause, TimeSpan.Zero, clock)
        {
        }

        /// <summary>
        /// Procedural constructor for <see cref="DeadlineFailureDetector"/>
        /// </summary>
        /// <param name="acceptableHeartbeatPause">Duration corresponding to number of potentially lost/delayed
        /// heartbeats that will be accepted before considering it to be an anomaly.
        /// This margin is important to be able to survive sudden, occasional, pauses in heartbeat
        /// arrivals, due to for example garbage collect or network drop.</param>
        /// <param name="heartbeatInterval"></param>
        /// <param name="clock">The clock, returning current time in milliseconds, but can be faked for testing purposes. It is only used for measuring intervals (duration).</param>
        public DeadlineFailureDetector(
            TimeSpan acceptableHeartbeatPause,
            TimeSpan heartbeatInterval,
            Clock clock = null)
        {
            if (acceptableHeartbeatPause <= TimeSpan.Zero)
            {
                throw new ArgumentException("failure-detector.acceptable-heartbeat-pause must be >= 0s");
            }

            if (heartbeatInterval <= TimeSpan.Zero)
            {
                throw new ArgumentException("failure-detector.heartbeat-interval must be > 0s");
            }

            _clock = clock ?? DefaultClock;
            _deadlineMillis = Convert.ToInt64(acceptableHeartbeatPause.TotalMilliseconds + heartbeatInterval.TotalMilliseconds);
        }

        /// <summary>
        /// Constructor that reads parameters from an Akka <see cref="Config"/> section.
        /// Expects property 'acceptable-heartbeat-pause'.
        /// </summary>
        /// <param name="config"></param>
        /// <param name="ev"></param>
        public DeadlineFailureDetector(Config config, EventStream ev) 
            : this(
                  config.GetTimeSpan("acceptable-heartbeat-pause"),
                  config.GetTimeSpan("heartbeat-interval")) { }

        public override bool IsAvailable => IsAvailableTicks(_clock());

        private bool IsAvailableTicks(long timestamp)
        {
            if (_active) return (Interlocked.Read(ref _heartbeatTimestamp) + _deadlineMillis) > timestamp;
            return true; //treat unmanaged connections, e.g. with zero heartbeats, as healthy connections
        }

        public override bool IsMonitoring => _active;

        public override void HeartBeat()
        {
            Interlocked.Exchange(ref _heartbeatTimestamp, _clock());
            _active = true;
        }
    }
}
