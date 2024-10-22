﻿//-----------------------------------------------------------------------
// <copyright file="ClusterLogSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.Util.Internal;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Akka.Cluster.Tests
{
    public abstract class ClusterLogSpec : AkkaSpec
    {
        public const string Config = @"    
            akka.cluster {
              auto-down-unreachable-after = 0s
              publish-stats-interval = 0s # always, when it happens
              run-coordinated-shutdown-when-down = off
            }
            akka.actor.provider = ""cluster""
            akka.remote.log-remote-lifecycle-events = off
            akka.remote.dot-netty.tcp.port = 0
            akka.loglevel = ""INFO""
            akka.loggers = [""Akka.TestKit.TestEventListener, Akka.TestKit""]";

        protected const string upLogMessage = " - event MemberUp";
        protected const string downLogMessage = " - event MemberDowned";
        protected readonly Address _selfAddress;
        protected readonly Cluster _cluster;

        internal ClusterReadView ClusterView { get { return _cluster.ReadView; } }

        protected ClusterLogSpec(ITestOutputHelper output, Config config = null)
            : base(config ?? Config, output)
        {
            _selfAddress = Sys.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress;
            _cluster = Cluster.Get(Sys);
        }

        protected async Task AwaitUpAsync()
        {
            await WithinAsync(TimeSpan.FromSeconds(10), async() =>
            {
                await AwaitConditionAsync(() => Task.FromResult(ClusterView.IsSingletonCluster));
                ClusterView.Self.Address.ShouldBe(_selfAddress);
                ClusterView.Members.Select(m => m.Address).ShouldBe(new Address[] { _selfAddress });
                await AwaitAssertAsync(() => ClusterView.Status.ShouldBe(MemberStatus.Up));
            });
        }

        /// <summary>
        /// The expected log info pattern to intercept after a <see cref="Cluster.Join(Address)"/>.
        /// </summary>
        protected async Task JoinAsync(string expected)
        {
            await EventFilter
                .Info(contains: expected)
                .ExpectOneAsync(10.Seconds(), async () => {
                    var tcs = new TaskCompletionSource<bool>();
                    _cluster.RegisterOnMemberUp(() =>
                    {
                        tcs.TrySetResult(true);
                    });
                    _cluster.Join(_selfAddress);
                    await tcs.Task.ShouldCompleteWithin(10.Seconds());
                });
        }

        /// <summary>
        /// The expected log info pattern to intercept after a <see cref="Cluster.Down(Address)"/>.
        /// </summary>
        /// <param name="expected"></param>
        protected async Task DownAsync(string expected)
        {
            await EventFilter
                .Info(contains: expected)
                .ExpectOneAsync(10.Seconds(), async () =>
                {
                    var tcs = new TaskCompletionSource<bool>();
                    _cluster.RegisterOnMemberRemoved(() =>
                    {
                        tcs.TrySetResult(true);
                    });
                    _cluster.Down(_selfAddress);
                    await tcs.Task.ShouldCompleteWithin(10.Seconds());
                });
        }
    }

    public class ClusterLogDefaultSpec : ClusterLogSpec
    {
        public ClusterLogDefaultSpec(ITestOutputHelper output)
            : base(output)
        { }

        [Fact]
        public async Task A_cluster_must_log_a_message_when_becoming_and_stopping_being_a_leader()
        {
            _cluster.Settings.LogInfo.ShouldBeTrue();
            _cluster.Settings.LogInfoVerbose.ShouldBeFalse();
            await JoinAsync("is the new leader");
            await AwaitUpAsync();
            await DownAsync("is no longer leader");
        }
    }

    public class ClusterLogVerboseDefaultSpec : ClusterLogSpec
    {
        public ClusterLogVerboseDefaultSpec(ITestOutputHelper output)
            : base(output)
        { }

        [Fact]
        public async Task A_cluster_must_not_log_verbose_cluster_events_by_default()
        {
            _cluster.Settings.LogInfoVerbose.ShouldBeFalse();
            await JoinAsync(upLogMessage).ShouldThrowWithin<XunitException>(11.Seconds());
            await AwaitUpAsync();
            await DownAsync(downLogMessage).ShouldThrowWithin<XunitException>(11.Seconds());
        }
    }

    public class ClusterLogVerboseEnabledSpec : ClusterLogSpec
    {
        public ClusterLogVerboseEnabledSpec(ITestOutputHelper output)
            : base(output, ConfigurationFactory
                  .ParseString("akka.cluster.log-info-verbose = on")
                  .WithFallback(ConfigurationFactory.ParseString(Config)))
        { }

        [Fact]
        public async Task A_cluster_must_log_verbose_cluster_events_when_log_info_verbose_is_on()
        {
            _cluster.Settings.LogInfoVerbose.ShouldBeTrue();
            await JoinAsync(upLogMessage);
            await AwaitUpAsync();
            await DownAsync(downLogMessage);
        }
    }
}
