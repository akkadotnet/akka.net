//-----------------------------------------------------------------------
// <copyright file="StartupWithOneThreadSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    public class StartupWithOneThreadSpec : AkkaSpec
    {
        private static readonly Config Configuration = ConfigurationFactory.ParseString(@"
            akka.actor.creation-timeout = 10s
            akka.actor.default-dispatcher.Type = ForkJoinDispatcher
            akka.actor.default-dispatcher.dedicated-thread-pool.thread-count = 1
            akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
            akka.remote.dot-netty.tcp.port = 0
        ");

        private long _startTime;

        public StartupWithOneThreadSpec() : base(Configuration)
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
        public void A_cluster_must_startup_with_one_dispatcher_thread()
        {
            // This test failed before fixing https://github.com/akkadotnet/akka.net/issues/1959 when adding a sleep before the
            // Await of GetClusterCoreRef in the Cluster extension constructor.
            // The reason was that other cluster actors were started too early and
            // they also tried to get the Cluster extension and thereby blocking
            // dispatcher threads.
            // Note that the Cluster extension is started via ClusterActorRefProvider
            // before ActorSystem.apply returns, i.e. in the constructor of AkkaSpec.
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
