//-----------------------------------------------------------------------
// <copyright file="ThrottleModeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Remote.Transport;
using Akka.TestKit;
using Akka.Util;
using Xunit;
using TokenBucket = Akka.Remote.Transport.TokenBucket;

namespace Akka.Remote.Tests.Transport
{
    public class ThrottleModeSpec : AkkaSpec
    {
        static readonly long HalfSecond = TimeSpan.FromSeconds(0.5).Ticks.ToNanos();

        [Fact]
        public void ThrottleMode_must_allow_consumption_of_infinite_amount_of_tokens_when_unthrottled()
        {
            var bucket = Unthrottled.Instance;
            bucket.TryConsumeTokens(0, 100).ShouldBe((Unthrottled.Instance, true));
            bucket.TryConsumeTokens(100000, 1000).ShouldBe((Unthrottled.Instance, true));
            bucket.TryConsumeTokens(1000000, 10000).ShouldBe((Unthrottled.Instance, true));
        }

        [Fact]
        public void ThrottleMode_must_deny_consumption_of_any_amount_of_tokens_when_blackhole()
        {
            var bucket = Blackhole.Instance;
            bucket.TryConsumeTokens(0, 100).ShouldBe((Blackhole.Instance, false));
            bucket.TryConsumeTokens(100000, 1000).ShouldBe((Blackhole.Instance, false));
            bucket.TryConsumeTokens(1000000, 10000).ShouldBe((Blackhole.Instance, false));
        }

        [Fact]
        public void ThrottleMode_must_in_tokenbucket_mode_allow_consuming_tokens_up_to_capacity()
        {
            var bucket = new TokenBucket(100, 100, 0L, 100);
            var bucket1 = bucket.TryConsumeTokens(0L, 10);
            bucket1.Item1.ShouldBe(new TokenBucket(100, 100, 0L, 90));
            bucket1.Item2.ShouldBeTrue();

            var bucket2 = bucket1.Item1.TryConsumeTokens(0L, 40);
            bucket2.Item1.ShouldBe(new TokenBucket(100, 100, 0L, 50));
            bucket2.Item2.ShouldBeTrue();

            var bucket3 = bucket2.Item1.TryConsumeTokens(0L, 50);
            bucket3.Item1.ShouldBe(new TokenBucket(100, 100, 0L, 0));
            bucket3.Item2.ShouldBeTrue();

            var bucket4 = bucket3.Item1.TryConsumeTokens(0L, 1);
            bucket4.Item1.ShouldBe(new TokenBucket(100, 100, 0L, 0));
            bucket4.Item2.ShouldBeFalse();
        }

        [Fact]
        public void ThrottleMode_must_accurately_replenish_tokens()
        {
            var bucket = new TokenBucket(100, 100, 0L, 0);
            var bucket1 = bucket.TryConsumeTokens(0L, 0);
            bucket1.Item1.ShouldBe(new TokenBucket(100, 100, 0L, 0));
            bucket1.Item2.ShouldBeTrue();

            var bucket2 = bucket1.Item1.TryConsumeTokens(HalfSecond, 0);
            bucket2.Item1.ShouldBe(new TokenBucket(100, 100, HalfSecond, 50));
            bucket2.Item2.ShouldBeTrue();

            var bucket3 = bucket2.Item1.TryConsumeTokens(HalfSecond * 2, 0);
            bucket3.Item1.ShouldBe(new TokenBucket(100, 100, HalfSecond * 2, 100));
            bucket3.Item2.ShouldBeTrue();

            var bucket4 = bucket3.Item1.TryConsumeTokens(HalfSecond * 3, 0);
            bucket4.Item1.ShouldBe(new TokenBucket(100, 100, HalfSecond * 3, 100));
            bucket4.Item2.ShouldBeTrue();
        }

        [Fact]
        public void ThrottleMode_must_accurately_interleave_replenish_and_consume()
        {
            var bucket = new TokenBucket(100, 100, 0L, 20);
            var bucket1 = bucket.TryConsumeTokens(0L, 10);
            bucket1.Item1.ShouldBe(new TokenBucket(100, 100, 0L, 10));
            bucket1.Item2.ShouldBeTrue();

            var bucket2 = bucket1.Item1.TryConsumeTokens(HalfSecond, 60);
            bucket2.Item1.ShouldBe(new TokenBucket(100, 100, HalfSecond, 0));
            bucket2.Item2.ShouldBeTrue();

            var bucket3 = bucket2.Item1.TryConsumeTokens(HalfSecond * 2, 40);
            bucket3.Item1.ShouldBe(new TokenBucket(100, 100, HalfSecond * 2, 10));
            bucket3.Item2.ShouldBeTrue();

            var bucket4 = bucket3.Item1.TryConsumeTokens(HalfSecond * 3, 70);
            bucket4.Item1.ShouldBe(new TokenBucket(100, 100, HalfSecond * 2, 10));
            bucket4.Item2.ShouldBeFalse();
        }

        [Fact]
        public void ThrottleMode_must_allow_oversized_packets_through_by_loaning()
        {
            var bucket = new TokenBucket(100, 100, 0L, 20);
            var bucket1 = bucket.TryConsumeTokens(0L, 30);
            bucket1.Item1.ShouldBe(new TokenBucket(100, 100, 0L, 20));
            bucket1.Item2.ShouldBeFalse();

            var bucket2 = bucket1.Item1.TryConsumeTokens(HalfSecond, 110);
            bucket2.Item1.ShouldBe(new TokenBucket(100, 100, HalfSecond, -40));
            bucket2.Item2.ShouldBeTrue();

            var bucket3 = bucket2.Item1.TryConsumeTokens(HalfSecond * 2, 20);
            bucket3.Item1.ShouldBe(new TokenBucket(100, 100, HalfSecond, -40));
            bucket3.Item2.ShouldBeFalse();

            var bucket4 = bucket3.Item1.TryConsumeTokens(HalfSecond * 3,20);
            bucket4.Item1.ShouldBe(new TokenBucket(100, 100, HalfSecond * 3, 40));
            bucket4.Item2.ShouldBeTrue();
        }
    }
}

