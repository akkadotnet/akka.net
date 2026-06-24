//-----------------------------------------------------------------------
// <copyright file="NodeChurnSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;

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
                  akka.cluster.run-coordinated-shutdown-when-down = on
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
        public async Task NodeChurnSpecs()
        {
            await Cluster_with_short_lived_members_must_setup_stable_nodes();
            await Cluster_with_short_lived_members_must_join_and_remove_transient_nodes_without_growing_gossip_payload();
        }

        public async Task Cluster_with_short_lived_members_must_setup_stable_nodes()
        {
            await WithinAsync(15.Seconds(), async () =>
            {
                var logListener = Sys.ActorOf(Props.Create(() => new LogListener(TestActor)), "logListener");
                Sys.EventStream.Subscribe(logListener, typeof(Info));
                Cluster.JoinSeedNodes(SeedNodes);
                await AwaitMembersUpAsync(Roles.Count);
                await EnterBarrierAsync("stable");
            });
        }

        public async Task Cluster_with_short_lived_members_must_join_and_remove_transient_nodes_without_growing_gossip_payload()
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

                await AwaitAllMembersUpAsync(systems);
                await EnterBarrierAsync("members-up-" + n);

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

                await AwaitRemovedAsync(systems, n);
                await EnterBarrierAsync("members-removed-" + n);

                // Terminate the transient systems asynchronously and concurrently. The old
                // synchronous Shutdown(node, verifySystemShutdown:true) helper blocked a
                // thread-pool thread inside Task.Wait() for up to its 5s budget while the
                // coordinated-shutdown pipeline itself needs the thread pool to make progress —
                // a sync-over-async self-starvation that produced the intermittent
                // "Failed to stop [NodeChurnSpec] within [00:00:05]" failures on slower CI agents.
                // Awaiting Terminate() frees the thread; WaitAsync preserves the verify-shutdown
                // semantics by throwing TimeoutException if a system fails to stop in time.
                // (Same idiom as QuickRestartSpec / DistributedPubSubRestartSpec.)
                await Task.WhenAll(systems.Select(s => s.Terminate())).WaitAsync(30.Seconds());

                Log.Info("end of round-" + n);
                // log listener will send to testActor if payload size exceed configured log-frame-size-exceeding
                await ExpectNoMsgAsync(2.Seconds());
            }
            await ExpectNoMsgAsync(5.Seconds());
        }

        private async Task AwaitAllMembersUpAsync(ImmutableList<ActorSystem> additionalSystems)
        {
            var numberOfMembers = Roles.Count + Roles.Count * additionalSystems.Count;
            await AwaitMembersUpAsync(numberOfMembers);
            await WithinAsync(20.Seconds(), async () =>
            {
                await AwaitAssertAsync(() =>
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

        private async Task AwaitRemovedAsync(ImmutableList<ActorSystem> additionalSystems, int round)
        {
            await AwaitMembersUpAsync(Roles.Count, timeout: 40.Seconds());
            await EnterBarrierAsync("removed-" + round);
            await WithinAsync(3.Seconds(), async () =>
            {
                await AwaitAssertAsync(() =>
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
