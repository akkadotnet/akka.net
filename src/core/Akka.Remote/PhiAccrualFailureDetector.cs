using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Util;

namespace Akka.Remote
{
    /**
     * Implementation of 'The Phi Accrual Failure Detector' by Hayashibara et al. as defined in their paper:
     * [http://ddg.jaist.ac.jp/pub/HDY+04.pdf]
     *
     * The suspicion level of failure is given by a value called φ (phi).
     * The basic idea of the φ failure detector is to express the value of φ on a scale that
     * is dynamically adjusted to reflect current network conditions. A configurable
     * threshold is used to decide if φ is considered to be a failure.
     *
     * The value of φ is calculated as:
     *
     * {{{
     * φ = -log10(1 - F(timeSinceLastHeartbeat)
     * }}}
     * where F is the cumulative distribution function of a normal distribution with mean
     * and standard deviation estimated from historical heartbeat inter-arrival times.
     */
    public class PhiAccrualFailureDetector : FailureDetector
    {
        private double _threshold;
        private int _maxSampleSize;
        private TimeSpan _minStdDeviation;
        private TimeSpan _acceptableHeartbeatPause;
        private TimeSpan _firstHeartbeatEstimate;
        private Clock _clock;

        /// <summary>
        /// Procedural constructor for PhiAccrualDetector
        /// </summary>
        /// <param name="threshold">A low threshold is prone to generate many wrong suspicions but ensures a quick detection in the event
        /// of a real crash. Conversely, a high threshold generates fewer mistakes but needs more time to detect actual crashes</param>
        /// <param name="maxSampleSize">Number of samples to use for calculation of mean and standard deviation of inter-arrival times.</param>
        /// <param name="minStdDeviation">Minimum standard deviation to use for the normal distribution used when calculating phi.
        /// Too low standard deviation might result in too much sensitivity for sudden, but normal, deviations 
        /// in heartbeat inter arrival times.</param>
        /// <param name="acceptableHeartbeatPause">Duration corresponding to number of potentially lost/delayed
        /// heartbeats that will be accepted before considering it to be an anomaly.
        /// This margin is important to be able to survive sudden, occasional, pauses in heartbeat
        /// arrivals, due to for example garbage collect or network drop.</param>
        /// <param name="firstHeartbeatEstimate">Bootstrap the stats with heartbeats that corresponds to
        /// to this duration, with a with rather high standard deviation (since environment is unknown
        /// in the beginning)</param>
        /// <param name="clock">The clock, returning current time in milliseconds, but can be faked for testing
        /// purposes. It is only used for measuring intervals (duration).</param>
        public PhiAccrualFailureDetector(double threshold, int maxSampleSize, TimeSpan minStdDeviation, TimeSpan acceptableHeartbeatPause, TimeSpan firstHeartbeatEstimate, Clock clock = null)
            : this(clock)
        {
            _threshold = threshold;
            _maxSampleSize = maxSampleSize;
            _minStdDeviation = minStdDeviation;
            _acceptableHeartbeatPause = acceptableHeartbeatPause;
            _firstHeartbeatEstimate = firstHeartbeatEstimate;
            state = new State(FirstHeartBeat, null);
        }

        /// <summary>
        /// Constructor that reads parameters from config.
        /// Expecting config properties named 'threshold', 'max-sample-size',
        /// 'min-std-deviation', 'acceptable-heartbeat-pause', and 'heartbeat-interval'.
        /// </summary>
        public PhiAccrualFailureDetector(Config config, EventStream ev)
            : this(DefaultClock)
        {
            _threshold = config.GetDouble("threshold");
            _maxSampleSize = config.GetInt("max-sample-size");
            _minStdDeviation = config.GetTimeSpan("min-std-deviation");
            _acceptableHeartbeatPause = config.GetTimeSpan("acceptable-heartbeat-pause");
            _firstHeartbeatEstimate = config.GetTimeSpan("heartbeat-interval");
            state = new State(FirstHeartBeat, null);
        }

        protected PhiAccrualFailureDetector(Clock clock)
        {
            _clock = clock ?? DefaultClock;
        }

        /// <summary>
        /// Guess statistics for first heartbeat,
        /// important so that connections with only one heartbeat becomes unavailable
        /// </summary>
        private HeartbeatHistory FirstHeartBeat
        {
            get
            {
                //bootstrap with 2 entries with rather high standard deviation
                var mean = (long)_firstHeartbeatEstimate.TotalMilliseconds;
                var stdDeviation = mean / 4;
                return HeartbeatHistory.Apply(_maxSampleSize) + (mean - stdDeviation) + (mean + stdDeviation);
            }
        }

        /// <summary>
        /// Uses volatile memory and immutability for lockless concurrency.
        /// </summary>
        internal class State
        {
            public State(HeartbeatHistory history, long? timeStamp)
            {
                TimeStamp = timeStamp;
                History = history;
            }

            public HeartbeatHistory History { get; private set; }

            public long? TimeStamp { get; private set; }
        }

        private AtomicReference<State> _state;

        private State state
        {
            get { return _state; }
            set { _state = value; }
        }

        public override bool IsAvailable
        {
            get { return IsTimeStampAvailable(_clock()); }
        }

        public override bool IsMonitoring
        {
            get { return state.TimeStamp.HasValue; }
        }

        public override void HeartBeat()
        {
            var timestamp = _clock();
            var oldState = state;
            HeartbeatHistory newHistory = null;

            if (!oldState.TimeStamp.HasValue)
            {
                //this is a heartbeat for a new resource
                //add starter records for this new resource
                newHistory = FirstHeartBeat;
            }
            else
            {
                //this is a known connection
                var interval = timestamp - oldState.TimeStamp.Value;
                //don't use the first heartbeat after failure for the history, since a long pause will skew the stats
                if (IsTimeStampAvailable(timestamp)) newHistory = (oldState.History + interval);
                else newHistory = oldState.History;
            }

            var newState = new State(newHistory, timestamp);
            //if we won the race then update else try again
            if(!_state.CompareAndSet(oldState, newState)) HeartBeat();
        }

        #region Internal methods

        private bool IsTimeStampAvailable(long timestamp)
        {
            return Phi(timestamp) < _threshold;
        }

        internal double CurrentPhi
        {
            get { return Phi(_clock()); }
        }

        internal double Phi(long timestamp)
        {
            var oldState = state;
            var oldTimestamp = oldState.TimeStamp;

            if (!oldTimestamp.HasValue)
                return 0.0d; //treat unmanaged connections, e.g. with zero heartbeats, as healthy connections
            else
            {
                var timeDiff = timestamp - oldTimestamp.Value;
                var history = oldState.History;
                var mean = history.Mean;
                var stdDeviation = EnsureValidStdDeviation(history.StdDeviation);
                return Phi(timeDiff, mean + AcceptableHeartbeatPauseMillis, stdDeviation);
            }
        }

        /// <summary>
        ///  Calculation of phi, derived from the Cumulative distribution function for
        /// N(mean, stdDeviation) normal distribution, given by
        /// 1.0 / (1.0 + math.exp(-y * (1.5976 + 0.070566 * y * y)))
        /// where y = (x - mean) / standard_deviation
        /// This is an approximation defined in β Mathematics Handbook (Logistic approximation).
        ///  Error is 0.00014 at +- 3.16
        /// The calculated value is equivalent to -log10(1 - CDF(y))
        /// </summary>
        internal double Phi(long timeDiff, double mean, double stdDeviation)
        {
            var y = (timeDiff - mean)/stdDeviation;
            var e = Math.Exp(-y*(1.5976 + 0.070566*y*y));
            if (timeDiff > mean)
                return -Math.Log10(e/(1.0d + e));
            else
                return -Math.Log10(1.0d - 1.0d/(1.0d + e));
        }

        private long MinStdDeviationMillis
        {
            get { return (long)_minStdDeviation.TotalMilliseconds; }
        }

        private long AcceptableHeartbeatPauseMillis
        {
            get { return (long)_acceptableHeartbeatPause.TotalMilliseconds; }
        }

        private double EnsureValidStdDeviation(double stdDeviation)
        {
            return Math.Max(stdDeviation, MinStdDeviationMillis);
        }

        #endregion
    }

    /// <summary>
    /// Holds the heartbeat statistics for a specific node <see cref="Address"/>.
    /// It is capped by the number of samples specified in 'maxSampleSize.'
    /// 
    /// The stats (mean, variance, stdDeviation) are not defined for empty
    /// <see cref="HeartbeatHistory"/>, i.e. throws Exception
    /// </summary>
    internal class HeartbeatHistory
    {
        private int _maxSampleSize;
        private List<long> _intervals;
        private long _intervalSum;
        private long _squaredIntervalSum;

        public HeartbeatHistory(int maxSampleSize, List<long> intervals, long intervalSum, long squaredIntervalSum)
        {
            _maxSampleSize = maxSampleSize;
            _intervals = intervals;
            _intervalSum = intervalSum;
            _squaredIntervalSum = squaredIntervalSum;

            if (maxSampleSize < 1)
                throw new ArgumentOutOfRangeException("maxSampleSize", string.Format("maxSampleSize must be >= 1, got {0}", maxSampleSize));
            if (intervalSum < 0L)
                throw new ArgumentOutOfRangeException("intervalSum", string.Format("intervalSum must be >= 0, got {0}", intervalSum));
            if (squaredIntervalSum < 0L)
                throw new ArgumentOutOfRangeException("squaredIntervalSum", string.Format("squaredIntervalSum must be >= 0, got {0}", squaredIntervalSum));
        }

        public double Mean
        {
            get { return ((double)_intervalSum / _intervals.Count); }
        }

        public double Variance
        {
            get { return ((double)_squaredIntervalSum / _intervals.Count) - (Mean * Mean); }
        }

        public double StdDeviation
        {
            get { return Math.Sqrt(Variance); }
        }

        public static HeartbeatHistory operator +(HeartbeatHistory history, long interval)
        {
            if (history._intervals.Count < history._maxSampleSize)
            {
                return new HeartbeatHistory(history._maxSampleSize, history._intervals.Concat(new[] { interval }).ToList(),
                    history._intervalSum + interval, history._squaredIntervalSum + Pow2(interval));
            }
            else
            {
                return DropOldest(history) + interval; //recurse
            }
        }

        private static HeartbeatHistory DropOldest(HeartbeatHistory history)
        {
            return new HeartbeatHistory(history._maxSampleSize, history._intervals.Skip(1).ToList(), history._intervalSum - history._intervals.First(), history._squaredIntervalSum - Pow2(history._intervals.First()));
        }

        private static long Pow2(long x)
        {
            return x * x;
        }

        #region Factory methods

        /// <summary>
        /// Create an empty <see cref="HeartbeatHistory"/> without any history.
        /// Can only be used as starting point for appending intervals.
        /// The stats (mean, variance, stdDeviation) are not defined for empty
        /// HeartbeatHistory and will throw DivideByZero exceptions
        /// </summary>
        public static HeartbeatHistory Apply(int maxSampleSize)
        {
            return new HeartbeatHistory(maxSampleSize, new List<long>(), 0L, 0L);
        }

        #endregion
    }
}
