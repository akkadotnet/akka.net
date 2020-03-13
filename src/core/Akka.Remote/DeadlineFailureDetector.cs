//-----------------------------------------------------------------------
// <copyright file="DeadlineFailureDetector.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Configuration;
using Akka.Event;

namespace Akka.Remote
{
    /// <summary>
    /// This class represents a <see cref="FailureDetector"/> that uses an absolute timeout
    /// of missing heartbeats to trigger unavailability.
    /// </summary>
    public class DeadlineFailureDetector : FailureDetector
    {
        private readonly Clock _clock;

        private long _heartbeatTimestamp = 0L; //not used until active (first heartbeat)
        private volatile bool _active = false;
        private readonly long _deadlineMillis;

        /// <summary>
        /// Obsolete. Use <see cref="DeadlineFailureDetector(TimeSpan, TimeSpan, Clock)"/> instead.
        /// </summary>
        /// <param name="acceptableHeartbeatPause">N/A</param>
        /// <param name="clock">N/A</param>
        [Obsolete("Use DeadlineFailureDetector(acceptableHeartbeatPause, heartbeatInterval, clock) instead. [1.1.2]")]
        public DeadlineFailureDetector(TimeSpan acceptableHeartbeatPause, Clock clock = null) 
            : this(acceptableHeartbeatPause, TimeSpan.Zero, clock)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DeadlineFailureDetector"/> class.
        /// </summary>
        /// <param name="acceptableHeartbeatPause">Duration corresponding to number of potentially lost/delayed
        /// heartbeats that will be accepted before considering it to be an anomaly.
        /// This margin is important to be able to survive sudden, occasional, pauses in heartbeat
        /// arrivals, due to for example garbage collect or network drop.</param>
        /// <param name="heartbeatInterval">The amount of time between heartbeats</param>
        /// <param name="clock">The clock, returning current time in milliseconds, but can be faked for testing purposes. It is only used for measuring intervals (duration).</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// This exception is thrown for the following reasons:
        /// <ul>
        /// <li>The specified <paramref name="acceptableHeartbeatPause"/> is less than zero.</li>
        /// <li>The specified <paramref name="heartbeatInterval"/> is less than zero</li>
        /// </ul>
        /// </exception>
        public DeadlineFailureDetector(
            TimeSpan acceptableHeartbeatPause,
            TimeSpan heartbeatInterval,
            Clock clock = null)
        {
            if (acceptableHeartbeatPause <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(acceptableHeartbeatPause), "failure-detector.acceptable-heartbeat-pause must be >= 0s");
            }

            if (heartbeatInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(heartbeatInterval), "failure-detector.heartbeat-interval must be > 0s");
            }

            _clock = clock ?? DefaultClock;
            _deadlineMillis = Convert.ToInt64(acceptableHeartbeatPause.TotalMilliseconds + heartbeatInterval.TotalMilliseconds);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DeadlineFailureDetector"/> class.
        /// </summary>
        /// <param name="config">
        /// The configuration used to configure this failure detector.
        /// <note>The configuration must define the 'akka.cluster.failure-detector.acceptable-heartbeat-pause' key.</note>
        /// </param>
        /// <param name="eventStream">N/A. This parameter is not used.</param>
        public DeadlineFailureDetector(Config config, EventStream eventStream) 
            : this(
                  config.GetTimeSpan("acceptable-heartbeat-pause", null),
                  config.GetTimeSpan("heartbeat-interval", null)) { }

        /// <summary>
        /// Determines whether the resource is considered to be up and healthy.
        /// </summary>
        public override bool IsAvailable => IsAvailableTicks(_clock());

        private bool IsAvailableTicks(long timestamp)
        {
            if (_active) return (Interlocked.Read(ref _heartbeatTimestamp) + _deadlineMillis) > timestamp;
            return true; //treat unmanaged connections, e.g. with zero heartbeats, as healthy connections
        }

        /// <summary>
        /// Determines whether the failure detector has received any heartbeats or has started monitoring the resource.
        /// </summary>
        public override bool IsMonitoring => _active;

        /// <summary>
        /// Notifies the failure detector that a heartbeat arrived from the monitored resource.
        /// </summary>
        public override void HeartBeat()
        {
            Interlocked.Exchange(ref _heartbeatTimestamp, _clock());
            _active = true;
        }
    }
}
