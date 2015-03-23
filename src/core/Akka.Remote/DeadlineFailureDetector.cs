using System;
using System.Threading;
using Akka.Configuration;
using Akka.Event;
using Akka.Util;

namespace Akka.Remote
{
    /// <summary>
    /// Implementation of failure detector using an absolute timeout of missing heartbeats
    /// to trigger unavailability
    /// </summary>
    public class DeadlineFailureDetector : FailureDetector
    {
        private TimeSpan _acceptableHeartbeatPause;
        private Clock _clock;

        /// <summary>
        /// Procedural constructor for <see cref="DeadlineFailureDetector"/>
        /// </summary>
        /// <param name="acceptableHeartbeatPause">Duration corresponding to number of potentially lost/delayed
        /// heartbeats that will be accepted before considering it to be an anomaly.
        /// This margin is important to be able to survive sudden, occasional, pauses in heartbeat
        /// arrivals, due to for example garbage collect or network drop.</param>
        /// <param name="clock">The clock, returning current time in milliseconds, but can be faked for testing
        /// purposes. It is only used for measuring intervals (duration).</param>
        public DeadlineFailureDetector(TimeSpan acceptableHeartbeatPause, Clock clock = null) : this(clock)
        {
            _acceptableHeartbeatPause = acceptableHeartbeatPause;
            _acceptableHeartbeatMillis = Convert.ToInt64(acceptableHeartbeatPause.TotalMilliseconds);
            Guard.Assert(_acceptableHeartbeatPause > TimeSpan.Zero, "acceptable-heartbeat-pause must be greater than zero");
        }

        /// <summary>
        /// Constructor that reads parameters from an Akka <see cref="Config"/> section.
        /// Expects property 'acceptable-heartbeat-pause'.
        /// </summary>
        /// <param name="config"></param>
        /// <param name="ev"></param>
        public DeadlineFailureDetector(Config config, EventStream ev) : this(config.GetTimeSpan("acceptable-heartbeat-pause")) { }

        protected DeadlineFailureDetector(Clock clock)
        {
            _clock = clock ?? DefaultClock;
        }

        private long _heartbeatTimestamp = 0L;
        private readonly long _acceptableHeartbeatMillis;
        private volatile bool _active = false;

        public override bool IsAvailable
        {
            get { return IsAvailableTicks(_clock()); }
        }

        public override bool IsMonitoring
        {
            get { return _active; }
        }

        public override void HeartBeat()
        {
            Interlocked.Exchange(ref _heartbeatTimestamp, _clock());
            _active = true;
        }

        private bool IsAvailableTicks(long timestamp)
        {
            if (_active) return (Interlocked.Read(ref _heartbeatTimestamp) + _acceptableHeartbeatMillis) > timestamp;
            return true; //treat unmanaged connections, e.g. with zero heartbeats, as healthy connections
        }
    }
}
