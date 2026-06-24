//-----------------------------------------------------------------------
// <copyright file="StartupWithOneThreadSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Akka.Util;
using Xunit;

namespace Akka.Cluster.Tests
{
    public sealed class StartupWithChannelExecutorSpec : AkkaSpec
    {
        private static readonly Config Configuration = ConfigurationFactory.ParseString(@"
            akka.actor.creation-timeout = 10s
            akka.actor.provider = cluster
            akka.actor.default-dispatcher.executor = channel-executor
            akka.actor.internal-dispatcher.executor = channel-executor
            akka.remote.default-remote-dispatcher.executor = channel-executor
            akka.remote.backoff-remote-dispatcher.executor = channel-executor            
        ").WithFallback(ConfigurationFactory.Default());

        private long _startTime;

        public StartupWithChannelExecutorSpec() : base(Configuration)
        {
            _startTime = MonotonicClock.GetTicks();
        }

        private Props TestProps
        {
            get
            {
                Action<IActorDsl> actor = (c =>
                {
                    c.ReceiveAny((o, context) => context.Sender.Tell(o));
                    c.OnPreStart = context =>
                    {
                        var log = context.GetLogger();
                        var cluster = Cluster.Get(context.System);
                        log.Debug("Started {0} {1}", cluster.SelfAddress, Thread.CurrentThread.Name);
                    };
                });
                return Props.Create(() => new Act(actor));
            }
        }

        [Fact]
        public void A_cluster_must_startup_with_channel_executor_dispatcher()
        {
            var totalStartupTime = TimeSpan.FromTicks(MonotonicClock.GetTicks() - _startTime).TotalMilliseconds;
            Assert.True(totalStartupTime < (Sys.Settings.CreationTimeout - TimeSpan.FromSeconds(2)).TotalMilliseconds);
            Sys.ActorOf(TestProps).Tell("hello");
            Sys.ActorOf(TestProps).Tell("hello");
            Sys.ActorOf(TestProps).Tell("hello");

            var cluster = Cluster.Get(Sys);
            totalStartupTime = TimeSpan.FromTicks(MonotonicClock.GetTicks() - _startTime).TotalMilliseconds;
            Assert.True(totalStartupTime < (Sys.Settings.CreationTimeout - TimeSpan.FromSeconds(2)).TotalMilliseconds);

            ExpectMsg("hello");
            ExpectMsg("hello");
            ExpectMsg("hello");
        }
    }
}
