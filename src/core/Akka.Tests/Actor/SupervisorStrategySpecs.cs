//-----------------------------------------------------------------------
// <copyright file="SupervisorStrategySpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Xunit;

namespace Akka.Tests.Actor
{
    public class SupervisorStrategySpecs
    {
        public static readonly object[][] RetriesTestData = new[]
        {
            new object[] { new int?(), -1 },
            new object[] { new int?(-1), -1 },
            new object[] { new int?(0), 0 },
            new object[] { new int?(5), 5 },
        };

        public static readonly object[][] TimeoutTestData = new[]
        {
            new object[] { new TimeSpan?(), -1 },
            new object[] { new TimeSpan?(System.Threading.Timeout.InfiniteTimeSpan), -1 },
            new object[] { new TimeSpan?(TimeSpan.FromMilliseconds(0)), 0 },
            new object[] { new TimeSpan?(TimeSpan.FromMilliseconds(100)), 100 },
            new object[] { new TimeSpan?(TimeSpan.FromMilliseconds(100).Add(TimeSpan.FromTicks(75))), 100 },
            new object[] { new TimeSpan?(TimeSpan.FromMilliseconds(10000)), 10000 },
        };

        [Theory]
        [MemberData(nameof(RetriesTestData))]
        public void A_constructed_OneForOne_supervisor_strategy_with_nullable_retries_has_the_expected_properties(int? retries, int expectedRetries)
        {
            var uut = new OneForOneStrategy(retries, null, exn => Directive.Restart);

            Assert.Equal(uut.MaxNumberOfRetries, expectedRetries);
        }

        [Theory]
        [MemberData(nameof(TimeoutTestData))]
        public void A_constructed_OneForOne_supervisor_strategy_with_nullable_timeouts_has_the_expected_properties(TimeSpan? timeout, int expectedTimeoutMilliseconds)
        {
            var uut = new OneForOneStrategy(-1, timeout, exn => Directive.Restart);

            Assert.Equal(uut.WithinTimeRangeMilliseconds, expectedTimeoutMilliseconds);
        }

        [Theory]
        [MemberData(nameof(RetriesTestData))]
        public void A_constructed_OneForOne_supervisor_strategy_with_nullable_retries_and_a_decider_has_the_expected_properties(int? retries, int expectedRetries)
        {
            var uut = new OneForOneStrategy(retries, null, Decider.From(Directive.Restart));

            Assert.Equal(uut.MaxNumberOfRetries, expectedRetries);
        }

        [Theory]
        [MemberData(nameof(TimeoutTestData))]
        public void A_constructed_OneForOne_supervisor_strategy_with_nullable_timeouts_and_a_decider_has_the_expected_properties(TimeSpan? timeout, int expectedTimeoutMilliseconds)
        {
            var uut = new OneForOneStrategy(-1, timeout, Decider.From(Directive.Restart));

            Assert.Equal(uut.WithinTimeRangeMilliseconds, expectedTimeoutMilliseconds);
        }

        [Theory]
        [MemberData(nameof(RetriesTestData))]
        public void A_constructed_AllForOne_supervisor_strategy_with_nullable_retries_has_the_expected_properties(int? retries, int expectedRetries)
        {
            var uut = new AllForOneStrategy(retries, null, exn => Directive.Restart);

            Assert.Equal(uut.MaxNumberOfRetries, expectedRetries);
        }

        [Theory]
        [MemberData(nameof(TimeoutTestData))]
        public void A_constructed_AllForOne_supervisor_strategy_with_nullable_timeouts_has_the_expected_properties(TimeSpan? timeout, int expectedTimeoutMilliseconds)
        {
            var uut = new AllForOneStrategy(-1, timeout, exn => Directive.Restart);

            Assert.Equal(uut.WithinTimeRangeMilliseconds, expectedTimeoutMilliseconds);
        }

        [Theory]
        [MemberData(nameof(RetriesTestData))]
        public void A_constructed_AllForOne_supervisor_strategy_with_nullable_retries_and_a_decider_has_the_expected_properties(int? retries, int expectedRetries)
        {
            var uut = new OneForOneStrategy(retries, null, Decider.From(Directive.Restart));

            Assert.Equal(uut.MaxNumberOfRetries, expectedRetries);
        }

        [Theory]
        [MemberData(nameof(TimeoutTestData))]
        public void A_constructed_AllForOne_supervisor_strategy_with_nullable_timeouts_and_a_decider_has_the_expected_properties(TimeSpan? timeout, int expectedTimeoutMilliseconds)
        {
            var uut = new OneForOneStrategy(-1, timeout, Decider.From(Directive.Restart));

            Assert.Equal(uut.WithinTimeRangeMilliseconds, expectedTimeoutMilliseconds);
        }
    }
}
