//-----------------------------------------------------------------------
// <copyright file="TimeoutSettingsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Coordination.Tests
{
    public class TimeoutSettingsSpec : AkkaSpec
    {
        public TimeoutSettingsSpec(ITestOutputHelper helper) : base(helper) { }

        private TimeoutSettings Conf(string overrides)
        {
            var c = ConfigurationFactory.ParseString(overrides)
                .WithFallback(LeaseProvider.DefaultConfig());
            return TimeoutSettings.Create(c);
        }

        #region Tests

        [Fact]
        public void TimeoutSettings_should_default_heartbeat_interval_to_heartbeat_timeout_div_10()
        {
            Conf(@"
                    heartbeat-timeout=100s
                    heartbeat-interval=""""
                    lease-operation-timeout=5s
                  ").HeartbeatInterval.ShouldBe(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public void TimeoutSettings_should_have_a_min_of_5s_for_heartbeat_interval()
        {
            Conf(@"
                    heartbeat-timeout=40s
                    heartbeat-interval=""""
                    lease-operation-timeout=5s
                  ").HeartbeatInterval.ShouldBe(TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void TimeoutSettings_should_allow_overriding_of_heartbeat_interval()
        {
            Conf(@"
                    heartbeat-timeout=100s
                    heartbeat-interval=20s
                    lease-operation-timeout=5s
                  ").HeartbeatInterval.ShouldBe(TimeSpan.FromSeconds(20));
        }

        [Fact]
        public void TimeoutSettings_should_not_allow_interval_to_be_greater_or_equal_to_half_the_interval()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                Conf(@"
                    heartbeat-timeout=100s
                    heartbeat-interval=50s
                    lease-operation-timeout=5s
                  ");
            }).Message.ShouldBe("heartbeat-interval must be less than half heartbeat-timeout");
        }

        #endregion
    }
}