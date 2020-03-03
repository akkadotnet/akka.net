//-----------------------------------------------------------------------
// <copyright file="IntervalBasedRateLimiterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Collections.Generic;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class IntervalBasedRateLimiterSpec : Akka.TestKit.Xunit2.TestKit
    {
        private readonly Source<int, NotUsed> _infiniteSource = Source.From(Enumerable.Range(1, int.MaxValue - 1));

        [Fact(Skip = "Racy")]
        public void IntervalBasedRateLimiter_should_limit_rate_of_messages_when_frequency_is_low_1_element_per_500ms()
        {
            TestCase(source: _infiniteSource,
                numOfElements: 6,
                maxBatchSize: 1,
                minInterval: TimeSpan.FromMilliseconds(500));
        }

        [Fact(Skip = "Racy")]
        public void IntervalBasedRateLimiter_should_limit_rate_of_messages_when_frequency_is_medium_10_elements_per_100ms()
        {
            TestCase(source: _infiniteSource,
                numOfElements: 300,
                maxBatchSize: 10,
                minInterval: TimeSpan.FromMilliseconds(100));
        }

        [Fact(Skip = "Racy")]
        public void IntervalBasedRateLimiter_should_limit_rate_of_messages_when_frequency_is_moderate_20_elements_per_100ms()
        {
            TestCase(source: _infiniteSource,
                numOfElements: 600,
                maxBatchSize: 20,
                minInterval: TimeSpan.FromMilliseconds(100));
        }

        [Fact(Skip = "Racy")]
        public void IntervalBasedRateLimiter_should_limit_rate_of_messages_when_frequency_is_moderate_200_elements_per_1000ms()
        {
            TestCase(source: _infiniteSource,
                numOfElements: 600,
                maxBatchSize: 200,
                minInterval: TimeSpan.FromMilliseconds(1000));
        }

        [Fact(Skip = "Racy")]
        public void IntervalBasedRateLimiter_should_limit_rate_of_messages_when_frequency_is_high_200_elements_per_100ms()
        {
            TestCase(source: _infiniteSource,
                numOfElements: 6000,
                maxBatchSize: 200,
                minInterval: TimeSpan.FromMilliseconds(100));
        }

        [Fact(Skip = "Racy")]
        public void IntervalBasedRateLimiter_should_limit_rate_of_messages_when_frequency_is_high_2_000_elements_per_1000ms()
        {
            TestCase(source: _infiniteSource,
                numOfElements: 6000,
                maxBatchSize: 2000,
                minInterval: TimeSpan.FromMilliseconds(1000));
        }

        [Fact]
        public void IntervalBasedRateLimiter_should_limit_rate_of_messages_when_frequency_is_very_high_50_000_elements_per_1000ms()
        {
            TestCase(source: _infiniteSource,
                numOfElements: 150000,
                maxBatchSize: 50000,
                minInterval: TimeSpan.FromMilliseconds(1000));
        }

        [Fact]
        public void IntervalBasedRateLimiter_should_limit_rate_of_messages_when_source_is_slow()
        {
            var slowInfiniteSource = _infiniteSource.Throttle(1, TimeSpan.FromMilliseconds(300), 1, ThrottleMode.Shaping);

            TestCase(source: slowInfiniteSource,
                numOfElements: 10,
                maxBatchSize: 1,
                minInterval: TimeSpan.FromMilliseconds(100));
        }

        private void TestCase<T>(Source<int, T> source,
            int numOfElements,
            int maxBatchSize,
            TimeSpan minInterval)
        {
            var flow = source
                .Take(numOfElements)
                .Via(IntervalBasedRateLimiter.Create<int>(minInterval, maxBatchSize))
                .Select(batch => (DateTime.Now.Ticks, batch))
                .RunWith(this.SinkProbe<(long, IEnumerable<int>)>(), Sys.Materializer());

            var timestamps = new List<long>();
            var batches = new List<IEnumerable<int>>();

            void CollectTimestampsAndBatches()
            {
                flow.Request(1);
                var e = flow.ExpectEvent();

                if (e is TestSubscriber.OnNext<(long, IEnumerable<int>)> onNext)
                {
                    timestamps.Add(onNext.Element.Item1);
                    batches.Add(onNext.Element.Item2);

                    CollectTimestampsAndBatches();
                }
            }

            CollectTimestampsAndBatches();

            var intervals = timestamps
                .Take(timestamps.Count - 1)
                .Zip(timestamps.Skip(1), (first, second) => TimeSpan.FromTicks(second - first));

            foreach (var interval in intervals)
                interval.Should().BeGreaterOrEqualTo(minInterval);

            batches.SelectMany(x => x).ShouldBeEquivalentTo(Enumerable.Range(1, numOfElements), o => o.WithStrictOrdering());
            batches.Count.Should().BeOneOf(numOfElements / maxBatchSize, numOfElements / maxBatchSize + 1);
        }
    }
}
