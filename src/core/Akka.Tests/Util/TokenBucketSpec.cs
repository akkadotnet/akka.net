//-----------------------------------------------------------------------
// <copyright file="TokenBucketSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using Akka.TestKit;
using Akka.Util;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Util
{
    public class TokenBucketSpec : AkkaSpec
    {
        private readonly ITestOutputHelper _output;

        public TokenBucketSpec(ITestOutputHelper output) : base(output)
        {
            _output = output;
        }

        [Fact]
        public void A_TokenBucket_must_start_full()
        {
            var bucket = new TestBucket(10,1);
            bucket.Init();

            bucket.Offer(1).ShouldBe(0);
            bucket.Offer(1).ShouldBe(0);
            bucket.Offer(1).ShouldBe(0);
            bucket.Offer(7).ShouldBe(0);
            
            bucket.Offer(3).ShouldBe(3);
        }

        [Fact]
        public void A_TokenBucket_must_calculate_correctly_with_different_rates_and_capacities()
        {
            var bucketRate2 = new TestBucket(10, 2);
            bucketRate2.Init();
            
            bucketRate2.Offer(5).ShouldBe(0);
            bucketRate2.Offer(5).ShouldBe(0);
            bucketRate2.Offer(5).ShouldBe(10);

            var bucketRate3 = new TestBucket(8, 3);
            bucketRate3.Init();

            bucketRate3.Offer(5).ShouldBe(0);
            bucketRate3.Offer(5).ShouldBe(6);

            bucketRate3.SetCurrentTime(6);
            bucketRate3.Offer(3).ShouldBe(9);
        }

        [Fact]
        public void A_TokenBucket_must_allow_sending_elements_larger_than_capacity()
        {
            var bucket = new TestBucket(10, 2);
            bucket.Init();

            bucket.Offer(5).ShouldBe(0);
            bucket.Offer(20).ShouldBe(30);

            bucket.SetCurrentTime(30);
            bucket.Offer(1).ShouldBe(2);

            bucket.SetCurrentTime(34);
            bucket.Offer(1).ShouldBe(0);
            bucket.Offer(1).ShouldBe(2);
        }

        [Fact]
        public void A_TokenBucket_must_work_with_zero_capacity()
        {
            var bucket = new TestBucket(0, 2);
            bucket.Init();

            bucket.Offer(10).ShouldBe(20);

            bucket.SetCurrentTime(40);
            bucket.Offer(10).ShouldBe(20);
        }

        [Fact]
        public void A_TokenBucket_must_not_delay_if_rate_is_higher_than_production()
        {
            var bucket = new TestBucket(1, 10);
            bucket.Init();

            for (var time = 0; time <= 100; time+=10)
            {
                bucket.SetCurrentTime(time);
                bucket.Offer(1).ShouldBe(0);
            }
        }

        [Fact]
        public void A_TokenBucket_must_maintain_maximum_capacity()
        {
            var bucket = new TestBucket(10, 1);
            bucket.Init();

            bucket.Offer(10).ShouldBe(0);

            bucket.SetCurrentTime(100000);
            bucket.Offer(20).ShouldBe(10);
        }

        [Fact]
        public void A_TokenBucket_must_work_if_CurrentTime_is_negative()
        {
            var bucket = new TestBucket(10, 1);
            bucket.SetCurrentTime(-100); // Must be set before init()!
            bucket.Init();

            bucket.Offer(5).ShouldBe(0);
            bucket.Offer(10).ShouldBe(5);

            bucket.SetCurrentTime(bucket.CurrentTime + 10);
            bucket.Offer(5).ShouldBe(0);
        }

        [Fact]
        public void A_TokenBucket_must_work_if_CurrentTime_wraps_over()
        {
            var bucket = new TestBucket(10, 1);
            bucket.SetCurrentTime(long.MaxValue - 5); // Must be set before init()!
            bucket.Init();

            bucket.Offer(5).ShouldBe(0);
            bucket.Offer(10).ShouldBe(5);

            bucket.SetCurrentTime(bucket.CurrentTime + 10);
            bucket.Offer(5).ShouldBe(0);
        }

        [Fact]
        public void A_TokenBucket_must_maintain_equal_time_between_token_renewal_intervals()
        {
            var bucket = new TestBucket(5, 3);
            bucket.Init();

            bucket.Offer(10).ShouldBe(15);
            bucket.SetCurrentTime(16);
            // At this point there is no token in the bucket (we consumed it at T15) but the next token will arrive at T18!
            // A naive calculation would consider that there is 1 token needed hence we need to wait 3 units, but in fact
            // we only need to wait 2 units, otherwise we shift the whole token arrival sequence resulting in lower rates.
            //
            // 0   3   9  12  15  18  21  24  27
            // +---+---+---+---+---+---+---+---+
            //                 ^ ^
            //  emitted here --+ +---- currently here (T16)
            //

            bucket.Offer(1).ShouldBe(2);
            bucket.SetCurrentTime(19);
            // At 18 bucket is empty, and so is at 19. For a cost of 2 we need to wait until T24 which is 5 units.
            //
            // 0   3   9  12  15  18  21  24  27
            // +---+---+---+---+---+---+---+---+
            //                     ^ ^
            //      emptied here --+ +---- currently here (T19)
            //
            bucket.Offer(2).ShouldBe(5);

            // Another case
            var bucket2 = new TestBucket(10, 3);
            bucket2.Init();
            
            bucket2.SetCurrentTime(4);
            bucket2.Offer(6).ShouldBe(0);

            // 4 tokens remain and new tokens arrive at T6 and T9 so here we have 6 tokens remaining.
            // We need 1 more, which will arrive at T12
            bucket2.SetCurrentTime(10);
            bucket2.Offer(7).ShouldBe(2);
        }

        [Fact]
        public void A_TokenBucket_must_work_with_cost_of_zero()
        {
            var bucket = new TestBucket(10, 1);
            bucket.Init();

            // Can be called any number of times
            bucket.Offer(0);
            bucket.Offer(0);
            bucket.Offer(0);

            bucket.Offer(10).ShouldBe(0);

            // Bucket is empty now
            // Still can be called any number of times
            bucket.Offer(0);
            bucket.Offer(0);
            bucket.Offer(0);
        }

        [Fact]
        public void A_TokenBucket_must_work_with_very_slow_rates()
        {
            const long t = long.MaxValue >> 10;
            var bucket = new TestBucket(10, t);
            bucket.Init();

            bucket.Offer(20).ShouldBe(10*t);
            bucket.SetCurrentTime(bucket.CurrentTime + 10*t);

            // Collect 5 tokens
            bucket.SetCurrentTime(bucket.CurrentTime + 5 * t);

            bucket.Offer(4).ShouldBe(0);
            bucket.Offer(2).ShouldBe(t);
        }

        [Fact]
        [SuppressMessage("ReSharper", "ConditionIsAlwaysTrueOrFalse")]
#pragma warning disable 162
        public void A_TokenBucket_must_behave_exactly_as_the_ideal_token_bucket_if_offer_is_called_with_perfect_timing()
        {
            const bool debug = false;
            var random = new Random();

            foreach (var capacity in new[] {0, 1, 5, 10})
            {
                foreach (var period in new[] { 1, 3, 5 })
                {
                    foreach (var arrivalPeriod in new[] { 1, 3, 5 })
                    {
                        foreach (var startTime in new[] { long.MinValue, -1, 0, long.MaxValue })
                        {
                            foreach (var maxCost in new[] { 1, 5, 10 })
                            {
                                var bucket = new TestBucket(capacity, period);
                                bucket.SetCurrentTime(startTime);
                                bucket.Init();

                                var idealBucket = capacity;
                                var untilNextTick = period;
                                long untilNextElement = random.Next(arrivalPeriod) + 1;
                                var nextEmit = 0L;
                                var delaying = false;

                                for (var time = 0L; time <= 1000; time++)
                                {
                                    if (untilNextTick == 0)
                                    {
                                        untilNextTick = period;
                                        idealBucket = Math.Min(idealBucket + 1, capacity);
                                    }

                                    if(debug)
                                        _output.WriteLine($"T:{time}  bucket:{idealBucket}");

                                    if (delaying && idealBucket == 0)
                                    {
                                        // Actual emit time should equal to what the optimized token bucket calculates
                                        time.ShouldBe(nextEmit);
                                        untilNextElement = time + random.Next(arrivalPeriod);
                                        if(debug)
                                            _output.WriteLine("EMITTING");
                                        delaying = false;
                                    }

                                    if (untilNextElement == 0)
                                    {
                                        //Allow cost of zero
                                        var cost = random.Next(maxCost + 1);
                                        idealBucket -= cost; // This can go negative
                                        bucket.SetCurrentTime(startTime + time);
                                        var delay = bucket.Offer(cost);
                                        nextEmit = time + delay;

                                        if(debug)
                                            _output.WriteLine($"ARRIVAL cost: {cost} at: {nextEmit}");
                                        if (delay == 0)
                                        {
                                            (idealBucket >= 0).ShouldBe(true);
                                            untilNextElement = time + random.Next(arrivalPeriod);
                                        }
                                        else
                                            delaying = true;
                                    }

                                    untilNextTick--;
                                    untilNextElement--;
                                }
                            }
                        }
                    }
                }
            }
        }
#pragma warning restore 162

        private sealed class TestBucket : TokenBucket
        {
            public TestBucket(long capacity, long ticksBetweenTokens) : base(capacity, ticksBetweenTokens)
            {
            }

            private long _currentTime;

            public override long CurrentTime => _currentTime;

            public void SetCurrentTime(long time) => _currentTime = time;
        }
    }
}
