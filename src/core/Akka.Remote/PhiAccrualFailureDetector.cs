//-----------------------------------------------------------------------
// <copyright file="PhiAccrualFailureDetector.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Util;

namespace Akka.Remote
{
    /// <summary>
    /// Implementation of 'The Phi Accrual Failure Detector' by Hayashibara et al. as defined in their paper:
    /// [http://ddg.jaist.ac.jp/pub/HDY+04.pdf]
    ///
    /// The suspicion level of failure is given by a value called φ (phi).
    /// The basic idea of the φ failure detector is to express the value of φ on a scale that
    /// is dynamically adjusted to reflect current network conditions. A configurable
    /// threshold is used to decide if φ is considered to be a failure.
    ///
    /// The value of φ is calculated as:
    ///
    /// <code>
    /// φ = -log10(1 - F(timeSinceLastHeartbeat)
    /// </code>
    /// 
    /// where F is the cumulative distribution function of a normal distribution with mean
    /// and standard deviation estimated from historical heartbeat inter-arrival times.
    /// </summary>
    public class PhiAccrualFailureDetector : FailureDetector
    {
        private readonly double _threshold;
        private readonly int _maxSampleSize;
        private TimeSpan _minStdDeviation;
        private TimeSpan _acceptableHeartbeatPause;
        private TimeSpan _firstHeartbeatEstimate;
        private readonly Clock _clock;

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
        /// <param name="config">The HOCON configuration for the failure detector.</param>
        /// <param name="ev">The <see cref="EventStream"/> for this <see cref="ActorSystem"/>.</param>
        public PhiAccrualFailureDetector(Config config, EventStream ev)
            : this(DefaultClock)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<PhiAccrualFailureDetector>();

            _threshold = config.GetDouble("threshold", 0);
            _maxSampleSize = config.GetInt("max-sample-size", 0);
            _minStdDeviation = config.GetTimeSpan("min-std-deviation", null);
            _acceptableHeartbeatPause = config.GetTimeSpan("acceptable-heartbeat-pause", null);
            _firstHeartbeatEstimate = config.GetTimeSpan("heartbeat-interval", null);
            state = new State(FirstHeartBeat, null);
            EventStream = ev ?? Option<EventStream>.None;
        }

        /// <summary>
        /// Protected constructor to be used for sub-classing only.
        /// </summary>
        /// <param name="clock">The clock used fo marking time.</param>
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

        private Option<EventStream> EventStream { get; }

        /// <summary>
        /// Address introduced as a mutable property in order to avoid shuffling around API signatures
        /// </summary>
        public string Address { get; set; } = "N/A";

        /// <summary>
        /// Uses volatile memory and immutability for lockless concurrency.
        /// </summary>
        internal class State
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="history">TBD</param>
            /// <param name="timeStamp">TBD</param>
            public State(HeartbeatHistory history, long? timeStamp)
            {
                TimeStamp = timeStamp;
                History = history;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public HeartbeatHistory History { get; private set; }

            /// <summary>
            /// TBD
            /// </summary>
            public long? TimeStamp { get; private set; }
        }

        private AtomicReference<State> _state;

        internal State state
        {
            get { return _state; }
            set { _state = value; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsAvailable
        {
            get { return IsTimeStampAvailable(_clock()); }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsMonitoring
        {
            get { return state.TimeStamp.HasValue; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void HeartBeat()
        {
            var timestamp = _clock();
            var oldState = state;
            HeartbeatHistory newHistory;

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
                if (IsTimeStampAvailable(timestamp))
                {
                    if (interval >= (AcceptableHeartbeatPauseMillis / 3 * 2) && EventStream.HasValue)
                    {
                        EventStream.Value.Publish(new Warning(ToString(), GetType(),
                            $"heartbeat interval is growing too large for address {Address}: {interval} millis"));
                    }
                    newHistory = (oldState.History + interval);
                }
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

        /// <summary>
        /// TBD
        /// </summary>
        internal double CurrentPhi
        {
            get { return Phi(_clock()); }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="timestamp">TBD</param>
        /// <returns>TBD</returns>
        internal double Phi(long timestamp)
        {
            var oldState = state;
            var oldTimestamp = oldState.TimeStamp;

            if (!oldTimestamp.HasValue)
                return 0.0d; //treat unmanaged connections, e.g. with zero heartbeats, as healthy connections
            else
            {
                unchecked // in the extremely rare event of a clock roll-over
                {
                    var timeDiff = timestamp - oldTimestamp.Value;
                    var history = oldState.History;
                    var mean = history.Mean;
                    var stdDeviation = EnsureValidStdDeviation(history.StdDeviation);
                    return Phi(timeDiff, mean + AcceptableHeartbeatPauseMillis, stdDeviation);
                }
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
        /// <param name="timeDiff">TBD</param>
        /// <param name="mean">TBD</param>
        /// <param name="stdDeviation">TBD</param>
        /// <returns>TBD</returns>
        internal double Phi(long timeDiff, double mean, double stdDeviation)
        {
            var y = (timeDiff - mean)/stdDeviation;
            var e = Math.Exp(-y*(1.5976 + 0.070566*y*y));
            if (timeDiff > mean)
                return -Math.Log10(e/(1.0d + e));
            else
                return -Math.Log10(1.0d - 1.0d/(1.0d + e));
        }

        private double MinStdDeviationMillis
        {
            get { return _minStdDeviation.TotalMilliseconds; }
        }

        private double AcceptableHeartbeatPauseMillis
        {
            get { return _acceptableHeartbeatPause.TotalMilliseconds; }
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
    internal readonly struct HeartbeatHistory
    {
        private readonly int _maxSampleSize;
        private readonly long _intervalSum;
        private readonly long _squaredIntervalSum;

        /// <summary>
        /// Creates a new <see cref="HeartbeatHistory"/> instance.
        /// </summary>
        /// <param name="maxSampleSize">The maximum number of samples to retain. Older ones are dropped once intervals exceeds this value.</param>
        /// <param name="intervals">The range of recorded time intervals.</param>
        /// <param name="intervalSum">The sum of the recorded time intervals.</param>
        /// <param name="squaredIntervalSum">The squared sum of the intervals.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// This exception is thrown for the following reasons:
        /// <ul>
        /// <li>The specified <paramref name="maxSampleSize"/> is less than one.</li>
        /// <li>The specified <paramref name="intervalSum"/> is less than zero.</li>
        /// <li>The specified <paramref name="squaredIntervalSum"/> is less than zero.</li>
        /// </ul>
        /// </exception>
        public HeartbeatHistory(int maxSampleSize, ImmutableList<long> intervals, long intervalSum, long squaredIntervalSum)
        {
            _maxSampleSize = maxSampleSize;
            Intervals = intervals;
            _intervalSum = intervalSum;
            _squaredIntervalSum = squaredIntervalSum;

            if (maxSampleSize < 1)
                throw new ArgumentOutOfRangeException(nameof(maxSampleSize), $"maxSampleSize must be >= 1, got {maxSampleSize}");
            if (intervalSum < 0L)
                throw new ArgumentOutOfRangeException(nameof(intervalSum), $"intervalSum must be >= 0, got {intervalSum}");
            if (squaredIntervalSum < 0L)
                throw new ArgumentOutOfRangeException(nameof(squaredIntervalSum), $"squaredIntervalSum must be >= 0, got {squaredIntervalSum}");
        }

        public double Mean => ((double)_intervalSum / Intervals.Count);

        public double Variance => ((double)_squaredIntervalSum / Intervals.Count) - (Mean * Mean);

        public double StdDeviation => Math.Sqrt(Variance);

        public ImmutableList<long> Intervals { get; }

        /// <summary>
        /// Increments the <see cref="HeartbeatHistory"/>.
        /// </summary>
        /// <param name="history">The current history.</param>
        /// <param name="interval">The new interval which will be added.</param>
        /// <returns>A new heartbeat history instance with the added interval.</returns>
        public static HeartbeatHistory operator +(HeartbeatHistory history, long interval)
        {
            if (history.Intervals.Count < history._maxSampleSize)
            {
                return new HeartbeatHistory(history._maxSampleSize, history.Intervals.Add(interval),
                    history._intervalSum + interval, history._squaredIntervalSum + Pow2(interval));
            }
            else
            {
                return DropOldest(history) + interval; //recurse
            }
        }

        private static HeartbeatHistory DropOldest(HeartbeatHistory history)
        {
            return new HeartbeatHistory(history._maxSampleSize, history.Intervals.RemoveAt(0), history._intervalSum - history.Intervals.First(), 
                history._squaredIntervalSum - Pow2(history.Intervals.First()));
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
        /// <param name="maxSampleSize">The maximum number of samples to include in this history.</param>
        /// <returns>A new <see cref="HeartbeatHistory"/> instance.</returns>
        public static HeartbeatHistory Apply(int maxSampleSize)
        {
            return new HeartbeatHistory(maxSampleSize, ImmutableList<long>.Empty, 0L, 0L);
        }

        #endregion
    }
}

