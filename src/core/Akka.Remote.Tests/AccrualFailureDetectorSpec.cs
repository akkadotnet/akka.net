//-----------------------------------------------------------------------
// <copyright file="AccrualFailureDetectorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Remote.Tests
{

    public class AccrualFailureDetectorSpec : AkkaSpec
    {
        public static IEnumerable<(T, T)> Slide<T>(IEnumerable<T> values)
        {
            using (var iterator = values.GetEnumerator())
            {
                while (iterator.MoveNext())
                {
                    var first = iterator.Current;
                    var second = iterator.MoveNext() ? iterator.Current : default(T);
                    yield return (first, second);
                }
            }
        }

        [Fact]
        public void AccrualFailureDetector_must_use_good_enough_cumulative_distribution_function()
        {
            var fd = FailureDetectorSpecHelpers.CreateFailureDetector();
            ShouldBe(FailureDetectorSpecHelpers.cdf(fd.Phi(0, 0, 10)), 0.5d);
            ShouldBe(FailureDetectorSpecHelpers.cdf(fd.Phi(6L, 0, 10)), 0.7257d);
            ShouldBe(FailureDetectorSpecHelpers.cdf(fd.Phi(15L, 0, 10)), 0.9332d);
            ShouldBe(FailureDetectorSpecHelpers.cdf(fd.Phi(20L, 0, 10)), 0.97725d);
            ShouldBe(FailureDetectorSpecHelpers.cdf(fd.Phi(25L, 0, 10)), 0.99379d);
            ShouldBe(FailureDetectorSpecHelpers.cdf(fd.Phi(35L, 0, 10)), 0.99977d);
            ShouldBe(FailureDetectorSpecHelpers.cdf(fd.Phi(40L, 0, 10)), 0.99997d, 0.0001D);

            foreach (var pair in Slide(Enumerable.Range(0, 40)))
            {
                Assert.True(fd.Phi(pair.Item1, 0, 10) < fd.Phi(pair.Item2, 0, 10));
            }

            ShouldBe(FailureDetectorSpecHelpers.cdf(fd.Phi(22, 20.0, 3)), 0.7475d);
        }

        [Fact]
        public void AccrualFailureDetector_must_handle_outliers_without_losing_precision_or_hitting_exception()
        {
            var fd = FailureDetectorSpecHelpers.CreateFailureDetector();
            ShouldBe(fd.Phi(10L, 0, 1), 38.0D, 1.0D);
            ShouldBe(fd.Phi(-25L, 0, 1), 0.0d, 0.0d);
        }

        [Fact]
        public void AccrualFailureDetector_must_return_realistic_phi_values()
        {
            var fd = FailureDetectorSpecHelpers.CreateFailureDetector();
            var test = new Dictionary<int, double>() { { 0, 0.0 }, { 500, 0.1 }, { 1000, 0.3 }, { 1200, 1.6 }, { 1400, 4.7 }, { 1600, 10.8 }, { 1700, 15.3 } };
            foreach (var kv in test)
            {
                ShouldBe(fd.Phi(kv.Key, 1000.0, 100.0), kv.Value, 0.1);
            }

            //larger stdDeviation results => lower phi
            Assert.True(fd.Phi(1100, 1000.0, 500.0) < fd.Phi(1100, 1000.0, 100.0));
        }

        [Fact]
        public void AccrualFailureDetector_must_return_phi_value_of_zero_on_startup_for_each_address_when_no_heartbeats()
        {
            var fd = FailureDetectorSpecHelpers.CreateFailureDetector();
            Assert.Equal(0.0, fd.CurrentPhi);
            Assert.Equal(0.0, fd.CurrentPhi);
        }

        [Fact]
        public void AccrualFailureDetector_must_return_phi_based_on_guess_when_only_one_heartbeat()
        {
            var timeInterval = new List<long>() { 0, 1000, 1000, 1000, 1000 };
            var fd = FailureDetectorSpecHelpers.CreateFailureDetector(8.0d, 1000, TimeSpan.FromMilliseconds(100), TimeSpan.Zero,
                TimeSpan.FromSeconds(1), FailureDetectorSpecHelpers.FakeTimeGenerator(timeInterval));

            fd.HeartBeat();
            ShouldBe(fd.CurrentPhi, 0.3D, 0.2D);
            ShouldBe(fd.CurrentPhi, 4.5D, 0.3D);
            Assert.True(fd.CurrentPhi > 15.0D);
        }

        [Fact]
        public void AccrualFailureDetector_must_return_phi_value_using_first_interval_after_second_heartbeat()
        {
            var timeInterval = new List<long>() { 0, 100, 100, 100, 100 };
            var fd = FailureDetectorSpecHelpers.CreateFailureDetector(8.0d, 1000, TimeSpan.FromMilliseconds(100), TimeSpan.Zero,
                TimeSpan.FromSeconds(1), FailureDetectorSpecHelpers.FakeTimeGenerator(timeInterval));

            fd.HeartBeat();
            Assert.True(fd.CurrentPhi > 0.0d);
            fd.HeartBeat();
            Assert.True(fd.CurrentPhi > 0.0d);
        }

        [Fact]
        public void AccrualFailureDetector_must_mark_node_as_monitored_after_a_series_of_successful_heartbeats()
        {
            var timeInterval = new List<long>() { 0, 1000, 100, 100 };
            var fd = FailureDetectorSpecHelpers.CreateFailureDetector(8.0d, 1000, TimeSpan.FromMilliseconds(100), TimeSpan.Zero,
                TimeSpan.FromSeconds(1), FailureDetectorSpecHelpers.FakeTimeGenerator(timeInterval));

            Assert.False(fd.IsMonitoring);
            fd.HeartBeat();
            fd.HeartBeat();
            fd.HeartBeat();
            Assert.True(fd.IsMonitoring);
            Assert.True(fd.IsAvailable);
        }

        [Fact]
        public void AccrualFailureDetector_must_mark_node_as_dead_if_heartbeats_are_missed()
        {
            var timeInterval = new List<long>() { 0, 1000, 100, 100, 7000 };
            var fd = FailureDetectorSpecHelpers.CreateFailureDetector(3.0d, 1000, TimeSpan.FromMilliseconds(100), TimeSpan.Zero,
                TimeSpan.FromSeconds(1), FailureDetectorSpecHelpers.FakeTimeGenerator(timeInterval));

            fd.HeartBeat(); //0
            fd.HeartBeat(); //1000
            fd.HeartBeat(); //1100
            Assert.True(fd.IsAvailable); //1200
            Assert.False(fd.IsAvailable); //8200
        }

        [Fact]
        public void AccrualFailureDetector_must_mark_node_as_available_if_it_starts_heartbeats_again_after_being_marked_dead_due_to_detection_of_failure
            ()
        {
            //1000 regular intervals, 5 minute pause, then a short pause again that should trigger unreachable again
            var regularIntervals = new List<long>() { 0L }.Concat(Enumerable.Repeat(1000L, 999)).ToList();
            var timeIntervals = regularIntervals.Concat(new[] { (5 * 60 * 1000L), 100L, 900L, 100L, 7000L, 100L, 900L, 100L, 900L }).ToList();
            var fd = FailureDetectorSpecHelpers.CreateFailureDetector(8.0d, 1000, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(1), FailureDetectorSpecHelpers.FakeTimeGenerator(timeIntervals));
            for (var i = 0; i < 1000; i++) fd.HeartBeat();
            Assert.False(fd.IsAvailable); //after the long pause
            fd.HeartBeat();
            Assert.True(fd.IsAvailable);
            fd.HeartBeat();
            Assert.False(fd.IsAvailable); //after the 7 seconds pause
            fd.HeartBeat();
            Assert.True(fd.IsAvailable);
            fd.HeartBeat();
            Assert.True(fd.IsAvailable);
        }

        [Fact]
        public void AccrualFailureDetector_must_accept_some_configured_missing_heartbeats()
        {
            var timeIntervals = new List<long>() { 0L, 1000L, 1000L, 1000L, 4000L, 1000L, 1000L };
            var fd = FailureDetectorSpecHelpers.CreateFailureDetector(8.0d, 1000, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(3), //3 seconds acceptableLostDuration
                TimeSpan.FromSeconds(1), FailureDetectorSpecHelpers.FakeTimeGenerator(timeIntervals));

            fd.HeartBeat();
            fd.HeartBeat();
            fd.HeartBeat();
            fd.HeartBeat();
            Assert.True(fd.IsAvailable);
            fd.HeartBeat();
            Assert.True(fd.IsAvailable);

        }

        [Fact]
        public void AccrualFailureDetector_must_fail_after_configured_acceptable_missing_heartbeats()
        {
            var timeIntervals = new List<long>() { 0, 1000, 1000, 1000, 1000, 1000, 500, 500, 5000 };
            var fd = FailureDetectorSpecHelpers.CreateFailureDetector(8.0d, 1000, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(1), FailureDetectorSpecHelpers.FakeTimeGenerator(timeIntervals));

            fd.HeartBeat();
            fd.HeartBeat();
            fd.HeartBeat();
            fd.HeartBeat();
            fd.HeartBeat();
            fd.HeartBeat();
            Assert.True(fd.IsAvailable);
            fd.HeartBeat();
            Assert.False(fd.IsAvailable);
        }

        [Fact]
        public void AccrualFailureDetector_must_use_maxSampleSize_heartbeats()
        {
            var timeIntervals = new List<long>() { 0, 100, 100, 100, 100, 600, 500, 500, 500, 500, 500 };
            var fd = FailureDetectorSpecHelpers.CreateFailureDetector(8.0d, 3, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(1), FailureDetectorSpecHelpers.FakeTimeGenerator(timeIntervals));

            // 100 ms interval
            fd.HeartBeat(); //0
            fd.HeartBeat(); //100
            fd.HeartBeat(); //200
            fd.HeartBeat(); //300
            var phi1 = fd.CurrentPhi; //400
            // 500 ms interval, should become same phi when 100 ms intervals have been dropped
            fd.HeartBeat(); //1000
            fd.HeartBeat(); //1500
            fd.HeartBeat(); //2000
            fd.HeartBeat(); //2500
            var phi2 = fd.CurrentPhi; //3000
            ShouldBe(phi1, phi2);
        }

        [Fact]
        public void StatisticsForHeartbeats_must_calculate_correct_mean_and_variance()
        {
            var samples = new[] { 100L, 200L, 125L, 340L, 130L };
            var stats = samples.Aggregate(HeartbeatHistory.Apply(20), (current, stat) => current + stat);
            ShouldBe(stats.Mean, 179.0D, 0.00001);
            ShouldBe(stats.Variance, 7584.0D, 0.00001);
        }

        [Fact]
        public void StatisticsForHeartbeats_must_have_zero_variance_for_one_sample()
        {
            var history = HeartbeatHistory.Apply(600) + 1000L;
            ShouldBe(history.Variance, 0.0, 0.00001D);
        }

        [Fact]
        public void StatisticsForHeartbeats_must_be_capped_by_the_specified_maxSampleSize()
        {
            var history3 = HeartbeatHistory.Apply(3) + 100 + 110 + 90;
            ShouldBe(history3.Mean, 100.0D, 0.00001D);
            ShouldBe(history3.Variance, 66.6666667D, 0.00001D);

            var history4 = history3 + 140;
            ShouldBe(history4.Mean, 113.333333D, 0.00001D);
            ShouldBe(history4.Variance, 422.222222D, 0.00001D);

            var history5 = history4 + 80;
            ShouldBe(history5.Mean, 103.333333D, 0.00001D);
            ShouldBe(history5.Variance, 688.88888889D, 0.00001D);
        }


        // handle an edge case when the Clock rolls over
        // see https://github.com/akkadotnet/akka.net/issues/2581
        [Fact]
        public void PhiAccrualHistory_can_roll_over()
        {
            unchecked
            {
                var absoluteTimes = new List<long>
                {
                    (Int64.MaxValue - 300),
                    (Int64.MaxValue - 200),
                    (Int64.MaxValue - 100),
                    (Int64.MaxValue),
                    (Int64.MaxValue + 100),
                    (Int64.MaxValue + 200),
                    (Int64.MaxValue + 300),
                };

                // compute intervals
                var timeIntervals = new List<long>();

                for (var i = 0; i < absoluteTimes.Count-1; i++)
                {
                    timeIntervals.Add(absoluteTimes[i+1] - absoluteTimes[i]);
                }

                var fd =
                    FailureDetectorSpecHelpers.CreateFailureDetector(
                        FailureDetectorSpecHelpers.FakeTimeGenerator(timeIntervals));
                foreach (var i in timeIntervals)
                {
                    fd.HeartBeat();
                    Assert.True(fd.IsAvailable);
                }
            }
        }

        [Fact]
        public void PhiAccrualHistory_must_work_with_MonotonicClock()
        {
            var fd =
                   FailureDetectorSpecHelpers.CreateFailureDetector();

            Assert.True(fd.IsAvailable);
            fd.HeartBeat();
            Assert.True(fd.IsAvailable);
            fd.HeartBeat();
            fd.HeartBeat();
            fd.HeartBeat();
            Assert.True(fd.IsAvailable);
        }

        /// <summary>
        /// Uses an epsilon value to compare between floating point numbers.
        /// Uses a default epsilon value of 0.001d
        /// </summary>
        protected void ShouldBe(double actual, double expected, double epsilon = 0.001d)
        {
            Assert.True(Math.Abs(actual - expected) <= epsilon, string.Format("Expected {0} but received {1}", expected, actual));
        }

    }


    /// <summary>
    /// Static helper class used for assisting with tests related to <see cref="FailureDetector"/>s.
    /// </summary>
    public static class FailureDetectorSpecHelpers
    {
        public static Clock FakeTimeGenerator(IList<long> timeIntervals)
        {
            var times = timeIntervals.Skip(1)
                .Aggregate(new List<long>() { timeIntervals.Head() },
                    (list, l) => list.Concat(new List<long>()
                    {
                        list.Last() + l
                    }).ToList());

            return () =>
            {
                var currentTime = times.Head();
                times = times.Skip(1).ToList();
                return currentTime;
            };
        }

        /// <summary>
        /// Uses the default values for creating a new failure detector
        /// </summary>
        public static PhiAccrualFailureDetector CreateFailureDetector(Clock clock = null)
        {
            return CreateFailureDetector(8.0d, 1000, TimeSpan.FromMilliseconds(100), TimeSpan.Zero,
                TimeSpan.FromSeconds(1), clock ?? FailureDetector.DefaultClock);
        }

        public static PhiAccrualFailureDetector CreateFailureDetector(double threshold, int maxSampleSize,
            TimeSpan minStdDeviation, TimeSpan acceptableLostDuration, TimeSpan firstHeartbeatEstimate, Clock clock)
        {
            return new PhiAccrualFailureDetector(threshold, maxSampleSize, minStdDeviation, acceptableLostDuration, firstHeartbeatEstimate, clock);
        }

        public static DefaultFailureDetectorRegistry<string> CreateFailureDetectorRegistry(double threshold, int maxSampleSize,
            TimeSpan minStdDeviation, TimeSpan acceptableLostDuration, TimeSpan firstHeartbeatEstimate, Clock clock)
        {
            return new DefaultFailureDetectorRegistry<string>(() => CreateFailureDetector(threshold, maxSampleSize, minStdDeviation, acceptableLostDuration, firstHeartbeatEstimate, clock));
        }

        public static DefaultFailureDetectorRegistry<string> CreateFailureDetectorRegistry(Clock clock = null)
        {
            return new DefaultFailureDetectorRegistry<string>(() => CreateFailureDetector(clock));
        }

        public static double cdf(double phi)
        {
            return 1.0d - Math.Pow(10, -phi);
        }
    }
}

