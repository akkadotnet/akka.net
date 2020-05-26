//-----------------------------------------------------------------------
// <copyright file="NodeChurnSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode
{
    public class NodeChurnConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public NodeChurnConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(false)
                .WithFallback(ConfigurationFactory.ParseString(@"
                  akka.cluster.auto-down-unreachable-after = 1s
                  akka.remote.log-frame-size-exceeding = 2000b
                  akka.remote.dot-netty.tcp.batching.enabled = false # disable batching
                "))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class NodeChurnSpec : MultiNodeClusterSpec
    {
        private class LogListener : ReceiveActor
        {
            private readonly IActorRef _testActor;

            public LogListener(IActorRef testActor)
            {
                _testActor = testActor;

                Receive<Info>(info => info.Message is string, info =>
                {
                    if (((string)info.Message).StartsWith("New maximum payload size for [akka.cluster.GossipEnvelope]"))
                    {
                        _testActor.Tell(info.Message);
                    }
                });
            }
        }

        private readonly NodeChurnConfig _config;
        private const int Rounds = 5;

        private ImmutableList<Address> SeedNodes
        {
            get
            {
                return ImmutableList.Create(GetAddress(_config.First), GetAddress(_config.Second), GetAddress(_config.Third));
            }
        }

        public NodeChurnSpec() : this(new NodeChurnConfig())
        {
        }

        protected NodeChurnSpec(NodeChurnConfig config) : base(config, typeof(NodeChurnSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public void NodeChurnSpecs()
        {
            Cluster_with_short_lived_members_must_setup_stable_nodes();
            Cluster_with_short_lived_members_must_join_and_remove_transient_nodes_without_growing_gossip_payload();
        }

        public void Cluster_with_short_lived_members_must_setup_stable_nodes()
        {
            Within(15.Seconds(), () =>
            {
                var logListener = Sys.ActorOf(Props.Create(() => new LogListener(TestActor)), "logListener");
                Sys.EventStream.Subscribe(logListener, typeof(Info));
                Cluster.JoinSeedNodes(SeedNodes);
                AwaitMembersUp(Roles.Count);
                EnterBarrier("stable");
            });
        }

        public void Cluster_with_short_lived_members_must_join_and_remove_transient_nodes_without_growing_gossip_payload()
        {
            // This test is configured with log-frame-size-exceeding and the LogListener
            // will send to the testActor if unexpected increase in message payload size.
            // It will fail after a while if vector clock entries of removed nodes are not pruned.
            for (var n = 1; n <= Rounds; n++)
            {
                Log.Info("round-" + n);
                var systems = Enumerable.Repeat(0,2).Select(_ => ActorSystem.Create(Sys.Name, Sys.Settings.Config)).ToImmutableList();
                
                foreach (var s in systems)
                {
                    MuteDeadLetters(s);
                    Cluster.Get(s).JoinSeedNodes(SeedNodes);
                }

                AwaitAllMembersUp(systems);
                EnterBarrier("members-up-" + n);

                foreach (var node in systems)
                {
                    if (n % 2 == 0)
                    {
                        Cluster.Get(node).Down(Cluster.Get(node).SelfAddress);
                    }
                    else
                    {
                        Cluster.Get(node).Leave(Cluster.Get(node).SelfAddress);
                    }
                }

                AwaitRemoved(systems, n);
                EnterBarrier("members-removed-" + n);
                foreach (var node in systems)
                {
                   Shutdown(node, verifySystemShutdown:true);
                }
                Log.Info("end of round-" + n);
                // log listener will send to testActor if payload size exceed configured log-frame-size-exceeding
                ExpectNoMsg(2.Seconds());
            }
            ExpectNoMsg(5.Seconds());
        }

        private void AwaitAllMembersUp(ImmutableList<ActorSystem> additionalSystems)
        {
            var numberOfMembers = Roles.Count + Roles.Count * additionalSystems.Count;
            AwaitMembersUp(numberOfMembers);
            Within(20.Seconds(), () =>
            {
                AwaitAssert(() =>
                {
                    additionalSystems.ForEach(s =>
                    {
                        var cluster = Cluster.Get(s);
                        cluster.State.Members.Count.Should().Be(numberOfMembers);
                        cluster.State.Members.All(c => c.Status == MemberStatus.Up).Should().BeTrue("All members should be up.");
                    });
                });
            });
        }

        private void AwaitRemoved(ImmutableList<ActorSystem> additionalSystems, int round)
        {
            AwaitMembersUp(Roles.Count, timeout: 40.Seconds());
            EnterBarrier("removed-" + round);
            Within(3.Seconds(), () =>
            {
                AwaitAssert(() =>
                {
                    additionalSystems.ForEach(s =>
                    {
                        Cluster.Get(s).IsTerminated.Should().BeTrue($"{Cluster.Get(s).SelfAddress}");
                    });
                });
            });
        }
    }
}
