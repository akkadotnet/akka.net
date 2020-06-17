//-----------------------------------------------------------------------
// <copyright file="ClusterSharding.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Sharding.Tests
{
    public class ShardedDaemonProcessSpec : AkkaSpec
    {
        private sealed class Stop
        {
            public static Stop Instance { get; } = new Stop();
            private Stop() { }
        }

        private sealed class Started
        {
            public int Id { get; }
            public IActorRef SelfRef { get; }

            public Started(int id, IActorRef selfRef)
            {
                Id = id;
                SelfRef = selfRef;
            }
        }

        private class MyActor : UntypedActor
        {
            public int Id { get; }
            public IActorRef Probe { get; }

            public static Props Props(int id, IActorRef probe) =>
                Actor.Props.Create(() => new MyActor(id, probe));

            public MyActor(int id, IActorRef probe)
            {
                Id = id;
                Probe = probe;
            }

            protected override void PreStart()
            {
                base.PreStart();
                Probe.Tell(new Started(Id, Context.Self));
            }

            protected override void OnReceive(object message)
            {
                if (message is Stop _)
                    Context.Stop(Self);
            }
        }

        private static Config GetConfig()
        {
            return ConfigurationFactory.ParseString(@"
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0
                akka.remote.dot-netty.tcp.hostname = 127.0.0.1

                # ping often/start fast for test
                akka.cluster.sharded-daemon-process.keep-alive-interval = 1s

                akka.coordinated-shutdown.terminate-actor-system = off
                akka.coordinated-shutdown.run-by-actor-system-terminate = off")
                .WithFallback(ClusterSharding.DefaultConfig())
                .WithFallback(ClusterSingletonProxy.DefaultConfig());
        }

        public ShardedDaemonProcessSpec()
            : base(GetConfig())
        { }

        [Fact]
        public void ShardedDaemonProcess_must_have_a_single_node_cluster_running_first()
        {
            var probe = CreateTestProbe();
            Cluster.Get(Sys).Join(Cluster.Get(Sys).SelfAddress);
            probe.AwaitAssert(() => Cluster.Get(Sys).SelfMember.Status.ShouldBe(MemberStatus.Up), TimeSpan.FromSeconds(3));
        }

        [Fact]
        public void ShardedDaemonProcess_must_start_N_actors_with_unique_ids()
        {
            Cluster.Get(Sys).Join(Cluster.Get(Sys).SelfAddress);

            var probe = CreateTestProbe();
            ShardedDaemonProcess.Get(Sys).Init("a", 5, id => MyActor.Props(id, probe.Ref));

            var started = probe.ReceiveN(5);
            started.Count.ShouldBe(5);
        }

        [Fact]
        public void ShardedDaemonProcess_must_restart_actors_if_they_stop()
        {
            Cluster.Get(Sys).Join(Cluster.Get(Sys).SelfAddress);

            var probe = CreateTestProbe();
            ShardedDaemonProcess.Get(Sys).Init("stop", 2, id => MyActor.Props(id, probe.Ref));

            foreach (var started in Enumerable.Range(0, 2).Select(_ => probe.ExpectMsg<Started>()))
                started.SelfRef.Tell(Stop.Instance);

            // periodic ping every 1s makes it restart
            Enumerable.Range(0, 2).Select(_ => probe.ExpectMsg<Started>(TimeSpan.FromSeconds(3)));
        }

        [Fact]
        public void ShardedDaemonProcess_must_not_run_if_the_role_does_not_match_node_role()
        {
            Cluster.Get(Sys).Join(Cluster.Get(Sys).SelfAddress);

            var probe = CreateTestProbe();
            var settings = ShardedDaemonProcessSettings.Create(Sys).WithShardingSettings(ClusterShardingSettings.Create(Sys).WithRole("workers"));
            ShardedDaemonProcess.Get(Sys).Init("roles", 3, id => MyActor.Props(id, probe.Ref), settings);

            probe.ExpectNoMsg();
        }

        // only used in documentation
        private class TagProcessor : ReceiveActor
        {
            public string Tag { get; }

            public static Props Props(string tag) =>
                Actor.Props.Create(() => new TagProcessor(tag));

            public TagProcessor(string tag) => Tag = tag;

            protected override void PreStart()
            {
                // start the processing ...
                base.PreStart();
                Context.System.Log.Debug("Starting processor for tag {0}", Tag);
            }
        }

        private void DocExample()
        {
            #region tag-processing
            var tags = new[] { "tag-1", "tag-2", "tag-3" };
            ShardedDaemonProcess.Get(Sys).Init("TagProcessors", tags.Length, id => TagProcessor.Props(tags[id]));
            #endregion
        }
    }
}
