//-----------------------------------------------------------------------
// <copyright file="FailureDetectorRegistrySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests
{
    
    public class FailureDetectorRegistrySpec : AkkaSpec
    {
        [Fact]
        public void FailureDetectorRegistry_must_mark_node_as_available_after_a_series_of_successful_heartbeats()
        {
            var timeIntervals = new List<long>() {0L, 1000L, 100L, 100L};
            var fd =
                FailureDetectorSpecHelpers.CreateFailureDetectorRegistry(
                    clock: FailureDetectorSpecHelpers.FakeTimeGenerator(timeIntervals));

            fd.Heartbeat("resource1");
            fd.Heartbeat("resource1");
            fd.Heartbeat("resource1");
            Assert.True(fd.IsAvailable("resource1"));
        }

        [Fact]
        public void FailureDetectorRegistry_must_mark_node_as_dead_if_heartbeats_are_missed()
        {
            var timeIntervals = new List<long>() { 0L, 1000L, 100L, 100L, 4000L, 3000L };
            var fd =
                FailureDetectorSpecHelpers.CreateFailureDetectorRegistry(
                    clock: FailureDetectorSpecHelpers.FakeTimeGenerator(timeIntervals));

            fd.Heartbeat("resource1"); //0
            fd.Heartbeat("resource1"); //1000
            fd.Heartbeat("resource1"); //1100
            Assert.True(fd.IsAvailable("resource1")); //1200
            fd.Heartbeat("resource2"); //5200, but unrelated resource
            Assert.False(fd.IsAvailable("resource1"));
        }

        [Fact]
        public void FailureDetectorRegistry_must_accept_some_configured_missing_heartbeats()
        {
            var timeIntervals = new List<long>() { 0, 1000, 1000, 1000, 4000, 1000, 1000 };
            var fd =
                FailureDetectorSpecHelpers.CreateFailureDetectorRegistry(8.0d, 1000, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(3), //changed to 3 seconds
                TimeSpan.FromSeconds(1), FailureDetectorSpecHelpers.FakeTimeGenerator(timeIntervals));

            fd.Heartbeat("resource1");
            fd.Heartbeat("resource1");
            fd.Heartbeat("resource1");
            fd.Heartbeat("resource1");
            Assert.True(fd.IsAvailable("resource1"));
            fd.Heartbeat("resource1");
            Assert.True(fd.IsAvailable("resource1"));
        }

        [Fact]
        public void FailureDetectorRegistry_must_fail_after_configured_acceptable_missing_heartbeats()
        {
            var timeIntervals = new List<long>() { 0, 1000, 1000, 1000, 1000, 1000, 500, 500, 5000 };
            var fd =
                FailureDetectorSpecHelpers.CreateFailureDetectorRegistry(8.0d, 1000, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(3), //changed to 3 seconds
                TimeSpan.FromSeconds(1), FailureDetectorSpecHelpers.FakeTimeGenerator(timeIntervals));

            fd.Heartbeat("resource1");
            fd.Heartbeat("resource1");
            fd.Heartbeat("resource1");
            fd.Heartbeat("resource1");
            fd.Heartbeat("resource1");
            fd.Heartbeat("resource1");
            Assert.True(fd.IsAvailable("resource1"));
            fd.Heartbeat("resource1");
            Assert.False(fd.IsAvailable("resource1"));
        }

        [Fact]
        public void FailureDetectorRegistry_must_mark_node_as_available_after_explicit_removal_of_connection()
        {
            var timeIntervals = new List<long>() { 0, 1000, 100, 100, 100 };
            var fd =
                FailureDetectorSpecHelpers.CreateFailureDetectorRegistry(
                    clock: FailureDetectorSpecHelpers.FakeTimeGenerator(timeIntervals));

            fd.Heartbeat("resource1");
            fd.Heartbeat("resource1");
            fd.Heartbeat("resource1");
            Assert.True(fd.IsAvailable("resource1"));
            fd.Remove("resource1");
            Assert.True(fd.IsAvailable("resource1"));
        }

        [Fact]
        public void FailureDetectorRegistry_must_mark_node_as_available_after_explicit_removal_of_connection_and_receiving_heartbeat_again
            ()
        {
            var timeIntervals = new List<long>() { 0, 1000, 100, 1100, 1100, 1100, 1100, 1100, 100 };
            var fd =
                FailureDetectorSpecHelpers.CreateFailureDetectorRegistry(
                    clock: FailureDetectorSpecHelpers.FakeTimeGenerator(timeIntervals));

            Assert.False(fd.IsMonitoring("resource1"));
            fd.Heartbeat("resource1"); //0
            fd.Heartbeat("resource1"); //1000
            fd.Heartbeat("resource1"); //1100
            Assert.True(fd.IsAvailable("resource1")); //2200
            Assert.True(fd.IsMonitoring("resource1"));

            fd.Remove("resource1");

            Assert.False(fd.IsMonitoring("resource1"));
            Assert.True(fd.IsAvailable("resource1")); //3300

            //receives a heartbeat from an explicitly removed node
            fd.Heartbeat("resource1"); //4400
            fd.Heartbeat("resource1"); //5500
            fd.Heartbeat("resource1"); //6600

            Assert.True(fd.IsAvailable("resource1")); //6700
            Assert.True(fd.IsMonitoring("resource1"));
        }
    }
}

