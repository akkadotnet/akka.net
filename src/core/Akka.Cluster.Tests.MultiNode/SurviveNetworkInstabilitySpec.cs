//-----------------------------------------------------------------------
// <copyright file="SurviveNetworkInstabilitySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.Util;
using Akka.Util.Internal;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode
{
     /*
     * N.B. - Regions are used for targeting by DocFx to include
     * code inside relevant documentation.
     */
    #region MultiNodeSpecConfig
    public class SurviveNetworkInstabilitySpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }
        public RoleName Fifth { get; }
        public RoleName Sixth { get; }
        public RoleName Seventh { get; }
        public RoleName Eighth { get; }

        public SurviveNetworkInstabilitySpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");
            Fifth = Role("fifth");
            Sixth = Role("sixth");
            Seventh = Role("seventh");
            Eighth = Role("eighth");

            CommonConfig = DebugConfig(false)
                .WithFallback(ConfigurationFactory.ParseString(@"
                    akka.remote.system-message-buffer-size = 100
                    akka.remote.dot-netty.tcp.connection-timeout = 10s
                "))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());

            TestTransport = true;
        }
        #endregion

        public class Echo : ReceiveActor
        {
            public Echo()
            {
                ReceiveAny(m => Sender.Tell(m));
            }
        }

        public class Targets
        {
            public ISet<IActorRef> Refs { get; }

            public Targets(ISet<IActorRef> refs)
            {
                Refs = refs;
            }
        }

        public class TargetsRegistered
        {
            public static readonly TargetsRegistered Instance = new TargetsRegistered();
            private TargetsRegistered() { }
        }

        public class Watcher : ReceiveActor
        {
            private ISet<IActorRef> _targets;

            public Watcher()
            {
                _targets = ImmutableHashSet<IActorRef>.Empty;

                Receive<Targets>(targets =>
                {
                    _targets = targets.Refs;
                    Sender.Tell(TargetsRegistered.Instance);
                });

                Receive<string>(s => s.Equals("boom"), e =>
                {
                    _targets.ForEach(x => Context.Watch(x));
                });

                Receive<Terminated>(_ => { });
            }
        }
    }

    public class SurviveNetworkInstabilitySpec : MultiNodeClusterSpec
    {
        private readonly SurviveNetworkInstabilitySpecConfig _config;

        public SurviveNetworkInstabilitySpec()
            : this(new SurviveNetworkInstabilitySpecConfig())
        {
        }

        protected SurviveNetworkInstabilitySpec(SurviveNetworkInstabilitySpecConfig config) : base(config, typeof(SurviveNetworkInstabilitySpec))
        {
            _config = config;
            Sys.ActorOf<SurviveNetworkInstabilitySpecConfig.Echo>("echo");
        }

        private void AssertUnreachable(params RoleName[] subjects)
        {
            var expected = subjects.Select(c => GetAddress(c)).ToImmutableHashSet();
            AwaitAssert(() => ClusterView.UnreachableMembers
                .Select(c => c.Address).Should().BeEquivalentTo(expected));
        }

        private void AssertCanTalk(params RoleName[] alive)
        {
            RunOn(() =>
            {
                AwaitAllReachable();
            }, alive);
            EnterBarrier("reachable-ok");

            RunOn(() =>
            {
                foreach (var to in alive)
                {
                    var sel = Sys.ActorSelection(new RootActorPath(GetAddress(to)) / "user" / "echo");
                    var msg = $"ping-{to}";
                    var p = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        sel.Tell(msg, p.Ref);
                        p.ExpectMsg(msg, 1.Seconds());
                    });
                    p.Ref.Tell(PoisonPill.Instance);
                }
            }, alive);
            EnterBarrier("ping-ok");
        }

        [MultiNodeFact]
        public void SurviveNetworkInstabilitySpecs()
        {
            A_Network_partition_tolerant_cluster_must_reach_initial_convergence();
            A_Network_partition_tolerant_cluster_must_heal_after_a_broken_pair();
            A_Network_partition_tolerant_cluster_must_heal_after_one_isolated_node();
            A_Network_partition_tolerant_cluster_must_heal_two_isolated_islands();
            A_Network_partition_tolerant_cluster_must_heal_after_unreachable_when_ring_is_changed();
            A_Network_partition_tolerant_cluster_must_mark_quarantined_node_with_reachability_status_Terminated();
            A_Network_partition_tolerant_cluster_must_continue_and_move_Joining_to_Up_after_downing_of_one_half();
        }

        public void A_Network_partition_tolerant_cluster_must_reach_initial_convergence()
        {
            AwaitClusterUp(_config.First, _config.Second, _config.Third, _config.Fourth, _config.Fifth);

            EnterBarrier("after-1");
            AssertCanTalk(_config.First, _config.Second, _config.Third, _config.Fourth, _config.Fifth);
        }

        public void A_Network_partition_tolerant_cluster_must_heal_after_a_broken_pair()
        {
            Within(45.Seconds(), () =>
            {
                RunOn(() =>
                {
                    TestConductor.Blackhole(_config.First, _config.Second, ThrottleTransportAdapter.Direction.Both).Wait();
                }, _config.First);
                EnterBarrier("blackhole-2");

                RunOn(() =>
                {
                    AssertUnreachable(_config.Second);
                }, _config.First);

                RunOn(() =>
                {
                    AssertUnreachable(_config.First);
                }, _config.Second);

                RunOn(() =>
                {
                    AssertUnreachable(_config.First, _config.Second);
                }, _config.Third, _config.Fourth, _config.Fifth);

                EnterBarrier("unreachable-2");

                RunOn(() =>
                {
                    TestConductor.PassThrough(_config.First, _config.Second, ThrottleTransportAdapter.Direction.Both).Wait();
                }, _config.First);
                EnterBarrier("repair-2");

                // This test illustrates why we can't ignore gossip from unreachable aggregated
                // status. If all third, fourth, and fifth has been infected by first and second
                // unreachable they must accept gossip from first and second when their
                // broken connection has healed, otherwise they will be isolated forever.
                EnterBarrier("after-2");
                AssertCanTalk(_config.First, _config.Second, _config.Third, _config.Fourth, _config.Fifth);
            });
        }

        public void A_Network_partition_tolerant_cluster_must_heal_after_one_isolated_node()
        {
            Within(45.Seconds(), () =>
            {
                var others = ImmutableArray.Create(_config.Second, _config.Third, _config.Fourth, _config.Fifth);
                RunOn(() =>
                {
                    foreach (var other in others)
                    {
                        TestConductor.Blackhole(_config.First, other, ThrottleTransportAdapter.Direction.Both).Wait();
                    }
                }, _config.First);
                EnterBarrier("blackhole-3");

                RunOn(() =>
                {
                    AssertUnreachable(others.ToArray());
                }, _config.First);

                RunOn(() =>
                {
                    AssertUnreachable(_config.First);
                }, others.ToArray());

                EnterBarrier("unreachable-3");

                RunOn(() =>
                {
                    foreach (var other in others)
                    {
                        TestConductor.PassThrough(_config.First, other, ThrottleTransportAdapter.Direction.Both).Wait();
                    }
                }, _config.First);
                EnterBarrier("repair-3");
                AssertCanTalk(others.Add(_config.First).ToArray());
            });
        }

        public void A_Network_partition_tolerant_cluster_must_heal_two_isolated_islands()
        {
            Within(45.Seconds(), () =>
            {
                var island1 = ImmutableArray.Create(_config.First, _config.Second);
                var island2 = ImmutableArray.Create(_config.Third, _config.Fourth, _config.Fifth);

                RunOn(() =>
                {
                    // split the cluster in two parts (first, second) / (third, fourth, fifth)
                    foreach (var role1 in island1)
                    {
                        foreach (var role2 in island2)
                        {
                            TestConductor.Blackhole(role1, role2, ThrottleTransportAdapter.Direction.Both).Wait();
                        }
                    }
                }, _config.First);
                EnterBarrier("blackhole-4");

                RunOn(() =>
                {
                    AssertUnreachable(island2.ToArray());
                }, island1.ToArray());

                RunOn(() =>
                {
                    AssertUnreachable(island1.ToArray());
                }, island2.ToArray());

                EnterBarrier("unreachable-4");

                RunOn(() =>
                {
                    foreach (var role1 in island1)
                    {
                        foreach (var role2 in island2)
                        {
                            TestConductor.PassThrough(role1, role2, ThrottleTransportAdapter.Direction.Both).Wait();
                        }
                    }
                }, _config.First);
                EnterBarrier("repair-4");

                AssertCanTalk(island1.AddRange(island2).ToArray());
            });
        }

        public void A_Network_partition_tolerant_cluster_must_heal_after_unreachable_when_ring_is_changed()
        {
            Within(60.Seconds(), () =>
            {
                var joining = ImmutableArray.Create(_config.Sixth, _config.Seventh);
                var others = ImmutableArray.Create(_config.Second, _config.Third, _config.Fourth, _config.Fifth);

                RunOn(() =>
                {
                    foreach (var role1 in joining.Add(_config.First))
                    {
                        foreach (var role2 in others)
                        {
                            TestConductor.Blackhole(role1, role2, ThrottleTransportAdapter.Direction.Both).Wait();
                        }
                    }
                }, _config.First);
                EnterBarrier("blackhole-5");

                RunOn(() =>
                {
                    AssertUnreachable(others.ToArray());
                }, _config.First);

                RunOn(() =>
                {
                    AssertUnreachable(_config.First);
                }, others.ToArray());

                EnterBarrier("unreachable-5");

                RunOn(() =>
                {
                    Cluster.Join(GetAddress(_config.First));

                    // let them join and stabilize heartbeating
                    Thread.Sleep(Dilated(5000.Milliseconds()));
                }, joining.ToArray());

                EnterBarrier("joined-5");

                RunOn(() =>
                {
                    AssertUnreachable(others.ToArray());
                }, joining.Add(_config.First).ToArray());

                // others doesn't know about the joining nodes yet, no gossip passed through
                RunOn(() =>
                {
                    AssertUnreachable(_config.First);
                }, others.ToArray());

                EnterBarrier("more-unreachable-5");

                RunOn(() =>
                {
                    foreach (var role1 in joining.Add(_config.First))
                    {
                        foreach (var role2 in others)
                        {
                            TestConductor.PassThrough(role1, role2, ThrottleTransportAdapter.Direction.Both).Wait();
                        }
                    }
                }, _config.First);
                EnterBarrier("repair-5");

                RunOn(() =>
                {
                    // eighth not joined yet
                    AwaitMembersUp(Roles.Count - 1, timeout: Remaining);
                }, joining.AddRange(others).Add(_config.First).ToArray());
                EnterBarrier("after-5");

                AssertCanTalk(joining.AddRange(others).Add(_config.First).ToArray());
            });
        }

        public void A_Network_partition_tolerant_cluster_must_mark_quarantined_node_with_reachability_status_Terminated()
        {
            Within(60.Seconds(), () =>
            {
                var others = ImmutableArray.Create(_config.First, _config.Third, _config.Fourth, _config.Fifth, _config.Sixth, _config.Seventh);

                RunOn(() =>
                {
                    Sys.ActorOf<SurviveNetworkInstabilitySpecConfig.Watcher>("watcher");

                    // undelivered system messages in RemoteChild on third should trigger QuarantinedEvent
                    Sys.EventStream.Subscribe(TestActor, typeof(QuarantinedEvent));
                }, _config.Third);
                EnterBarrier("watcher-created");

                RunOn(() =>
                {
                    var sysMsgBufferSize = Sys
                        .AsInstanceOf<ExtendedActorSystem>().Provider
                        .AsInstanceOf<RemoteActorRefProvider>().RemoteSettings.SysMsgBufferSize;

                    var refs = Vector.Fill<IActorRef>(sysMsgBufferSize + 1)(
                        () => Sys.ActorOf<SurviveNetworkInstabilitySpecConfig.Echo>()).ToImmutableHashSet();

                    Sys.ActorSelection(Node(_config.Third) / "user" / "watcher").Tell(new SurviveNetworkInstabilitySpecConfig.Targets(refs));
                    ExpectMsg<SurviveNetworkInstabilitySpecConfig.TargetsRegistered>();
                }, _config.Second);
                EnterBarrier("targets-registered");

                RunOn(() =>
                {
                    foreach (var role in others)
                    {
                        TestConductor.Blackhole(role, _config.Second, ThrottleTransportAdapter.Direction.Both).Wait();
                    }
                }, _config.First);
                EnterBarrier("blackhole-6");

                RunOn(() =>
                {
                    // this will trigger watch of targets on second, resulting in too many outstanding
                    // system messages and quarantine
                    Sys.ActorSelection("/user/watcher").Tell("boom");
                    Within(10.Seconds(), () =>
                    {
                        ExpectMsg<QuarantinedEvent>().Address.Should().Be(GetAddress(_config.Second));
                    });
                    Sys.EventStream.Unsubscribe(TestActor, typeof(QuarantinedEvent));
                }, _config.Third);
                EnterBarrier("quarantined");

                RunOn(() =>
                {
                    Thread.Sleep(2000.Milliseconds());

                    var secondUniqueAddress = Cluster.State.Members.SingleOrDefault(m => m.Address == GetAddress(_config.Second));
                    secondUniqueAddress.Should().NotBeNull(because: "2nd node should stay visible");
                    secondUniqueAddress?.Status.Should().Be(MemberStatus.Up, because: "2nd node should be Up");
                    
                    // second should be marked with reachability status Terminated
                    AwaitAssert(() => ClusterView.Reachability.Status(secondUniqueAddress?.UniqueAddress).Should().Be(Reachability.ReachabilityStatus.Terminated));
                }, others.ToArray());
                EnterBarrier("reachability-terminated");

                RunOn(() =>
                {
                    Cluster.Down(GetAddress(_config.Second));
                }, _config.Fourth);

                RunOn(() =>
                {
                    // second should be removed because of quarantine
                    AwaitAssert(() => ClusterView.Members.Select(c => c.Address).Should().NotContain(GetAddress(_config.Second)));
                    // and also removed from reachability table
                    AwaitAssert(() => ClusterView.Reachability.AllUnreachableOrTerminated.Should().BeEmpty());
                }, others.ToArray());
                EnterBarrier("removed-after-down");

                EnterBarrier("after-6");
                AssertCanTalk(others.ToArray());
            });
        }

        public void A_Network_partition_tolerant_cluster_must_continue_and_move_Joining_to_Up_after_downing_of_one_half()
        {
            Within(60.Seconds(), () =>
            {
                // note that second is already removed in previous step
                var side1 = ImmutableArray.Create(_config.First, _config.Third, _config.Fourth);
                var side1AfterJoin = side1.Add(_config.Eighth);
                var side2 = ImmutableArray.Create(_config.Fifth, _config.Sixth, _config.Seventh);

                RunOn(() =>
                {
                    foreach (var role1 in side1AfterJoin)
                    {
                        foreach (var role2 in side2)
                        {
                            TestConductor.Blackhole(role1, role2, ThrottleTransportAdapter.Direction.Both).Wait();
                        }
                    }
                }, _config.First);
                EnterBarrier("blackhole-7");

                RunOn(() =>
                {
                    AssertUnreachable(side2.ToArray());
                }, side1.ToArray());

                RunOn(() =>
                {
                    AssertUnreachable(side1.ToArray());
                }, side2.ToArray());

                EnterBarrier("unreachable-7");

                RunOn(() =>
                {
                    Cluster.Join(GetAddress(_config.Third));
                }, _config.Eighth);

                RunOn(() =>
                {
                    foreach (var role2 in side2)
                    {
                        Cluster.Down(GetAddress(role2));
                    }
                }, _config.Fourth);

                EnterBarrier("downed-7");

                RunOn(() =>
                {
                    // side2 removed
                    var expected = side1AfterJoin.Select(c => GetAddress(c)).ToImmutableHashSet();
                    AwaitAssert(() =>
                    {
                        RunOn(() =>
                        {
                            // repeat the downing in case it was not successful, which may
                            // happen if the removal was reverted due to gossip merge
                            foreach (var role2 in side2)
                            {
                                Cluster.Down(GetAddress(role2));
                            }
                        }, _config.Fourth);

                        ClusterView.Members.Select(c => c.Address).Should().BeEquivalentTo(expected);
                        ClusterView.Members.Where(m => m.Address.Equals(GetAddress(_config.Eighth))).Select(m => m.Status).FirstOrDefault().Should().Be(MemberStatus.Up);
                    });
                }, side1AfterJoin.ToArray());
                EnterBarrier("side2-removed");

                RunOn(() =>
                {
                    foreach (var role1 in side1AfterJoin)
                    {
                        foreach (var role2 in side2)
                        {
                            TestConductor.PassThrough(role1, role2, ThrottleTransportAdapter.Direction.Both).Wait();
                        }
                    }
                }, _config.First);
                EnterBarrier("repair-7");

                // side2 should not detect side1 as reachable again
                Thread.Sleep(10000);

                RunOn(() =>
                {
                    var expected = side1AfterJoin.Select(c => GetAddress(c)).ToImmutableHashSet();
                    ClusterView.Members.Select(c => c.Address).Should().BeEquivalentTo(expected);
                }, side1AfterJoin.ToArray());

                RunOn(() =>
                {
                    // side2 comes back but stays unreachable
                    var expected = side2.AddRange(side1).Select(c => GetAddress(c)).ToImmutableHashSet();
                    ClusterView.Members.Select(c => c.Address).Should().BeEquivalentTo(expected);
                    AssertUnreachable(side1.ToArray());
                }, side2.ToArray());

                EnterBarrier("after-7");
                AssertCanTalk(side1AfterJoin.ToArray());
            });
        }
    }
}
