//-----------------------------------------------------------------------
// <copyright file="SurviveNetworkInstabilitySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
using Akka.Remote;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.Util;
using Akka.Util.Internal;
using FluentAssertions;
using FluentAssertions.Extensions;

namespace Akka.Cluster.Tests.MultiNode;

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
        public static readonly TargetsRegistered Instance = new();
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

            Receive<string>(s => s.Equals("boom"), _ =>
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

    private async Task AssertUnreachable(params RoleName[] subjects)
    {
        var expected = subjects.Select(GetAddress).ToImmutableHashSet();
        await AwaitAssertAsync(() => ClusterView.UnreachableMembers
            .Select(c => c.Address).Should().BeEquivalentTo(expected));
    }

    private async Task AssertCanTalk(params RoleName[] alive)
    {
        await RunOnAsync(async () =>
        {
            await AwaitAllReachableAsync();
        }, alive);
        await EnterBarrierAsync("reachable-ok");

        await RunOnAsync(async () =>
        {
            foreach (var to in alive)
            {
                var sel = Sys.ActorSelection(new RootActorPath(GetAddress(to)) / "user" / "echo");
                var msg = $"ping-{to}";
                var p = CreateTestProbe();
                await AwaitAssertAsync(async () =>
                {
                    sel.Tell(msg, p.Ref);
                    await p.ExpectMsgAsync(msg, 1.Seconds());
                });
                p.Ref.Tell(PoisonPill.Instance);
            }
        }, alive);
        await EnterBarrierAsync("ping-ok");
    }

    [MultiNodeFact]
    public async Task SurviveNetworkInstabilitySpecs()
    {
        await A_Network_partition_tolerant_cluster_must_reach_initial_convergence();
        await A_Network_partition_tolerant_cluster_must_heal_after_a_broken_pair();
        await A_Network_partition_tolerant_cluster_must_heal_after_one_isolated_node();
        await A_Network_partition_tolerant_cluster_must_heal_two_isolated_islands();
        await A_Network_partition_tolerant_cluster_must_heal_after_unreachable_when_ring_is_changed();
        await A_Network_partition_tolerant_cluster_must_mark_quarantined_node_with_reachability_status_Terminated();
        await A_Network_partition_tolerant_cluster_must_continue_and_move_Joining_to_Up_after_downing_of_one_half();
    }

    public async Task A_Network_partition_tolerant_cluster_must_reach_initial_convergence()
    {
        await AwaitClusterUpAsync(CancellationToken.None, _config.First, _config.Second, _config.Third, _config.Fourth, _config.Fifth);

        await EnterBarrierAsync("after-1");
        await AssertCanTalk(_config.First, _config.Second, _config.Third, _config.Fourth, _config.Fifth);
    }

    public async Task A_Network_partition_tolerant_cluster_must_heal_after_a_broken_pair()
    {
        await WithinAsync(45.Seconds(), async () =>
        {
            await RunOnAsync(async () =>
            {
                await TestConductor.BlackholeAsync(_config.First, _config.Second, ThrottleTransportAdapter.Direction.Both);
            }, _config.First);
            await EnterBarrierAsync("blackhole-2");

            await RunOnAsync(async () =>
            {
                await AssertUnreachable(_config.Second);
            }, _config.First);

            await RunOnAsync(async () =>
            {
                await AssertUnreachable(_config.First);
            }, _config.Second);

            await RunOnAsync(async () =>
            {
                await AssertUnreachable(_config.First, _config.Second);
            }, _config.Third, _config.Fourth, _config.Fifth);

            await EnterBarrierAsync("unreachable-2");

            await RunOnAsync(async () =>
            {
                await TestConductor.PassThroughAsync(_config.First, _config.Second, ThrottleTransportAdapter.Direction.Both);
            }, _config.First);
            await EnterBarrierAsync("repair-2");

            // This test illustrates why we can't ignore gossip from unreachable aggregated
            // status. If all third, fourth, and fifth has been infected by first and second
            // unreachable they must accept gossip from first and second when their
            // broken connection has healed, otherwise they will be isolated forever.
            await EnterBarrierAsync("after-2");
            await AssertCanTalk(_config.First, _config.Second, _config.Third, _config.Fourth, _config.Fifth);
        });
    }

    public async Task A_Network_partition_tolerant_cluster_must_heal_after_one_isolated_node()
    {
        await WithinAsync(45.Seconds(), async () =>
        {
            var others = ImmutableArray.Create(_config.Second, _config.Third, _config.Fourth, _config.Fifth);
            await RunOnAsync(async () =>
            {
                foreach (var other in others)
                {
                    await TestConductor.BlackholeAsync(_config.First, other, ThrottleTransportAdapter.Direction.Both);
                }
            }, _config.First);
            await EnterBarrierAsync("blackhole-3");

            await RunOnAsync(async () =>
            {
                await AssertUnreachable(others.ToArray());
            }, _config.First);

            await RunOnAsync(async () =>
            {
                await AssertUnreachable(_config.First);
            }, others.ToArray());

            await EnterBarrierAsync("unreachable-3");

            await RunOnAsync(async () =>
            {
                foreach (var other in others)
                {
                    await TestConductor.PassThroughAsync(_config.First, other, ThrottleTransportAdapter.Direction.Both);
                }
            }, _config.First);
            await EnterBarrierAsync("repair-3");
            await AssertCanTalk(others.Add(_config.First).ToArray());
        });
    }

    public async Task A_Network_partition_tolerant_cluster_must_heal_two_isolated_islands()
    {
        await WithinAsync(45.Seconds(), async () =>
        {
            var island1 = ImmutableArray.Create(_config.First, _config.Second);
            var island2 = ImmutableArray.Create(_config.Third, _config.Fourth, _config.Fifth);

            await RunOnAsync(async () =>
            {
                // split the cluster in two parts (first, second) / (third, fourth, fifth)
                foreach (var role1 in island1)
                {
                    foreach (var role2 in island2)
                    {
                        await TestConductor.BlackholeAsync(role1, role2, ThrottleTransportAdapter.Direction.Both);
                    }
                }
            }, _config.First);
            await EnterBarrierAsync("blackhole-4");

            await RunOnAsync(async () =>
            {
                await AssertUnreachable(island2.ToArray());
            }, island1.ToArray());

            await RunOnAsync(async () =>
            {
                await AssertUnreachable(island1.ToArray());
            }, island2.ToArray());

            await EnterBarrierAsync("unreachable-4");

            await RunOnAsync(async () =>
            {
                foreach (var role1 in island1)
                {
                    foreach (var role2 in island2)
                    {
                        await TestConductor.PassThroughAsync(role1, role2, ThrottleTransportAdapter.Direction.Both);
                    }
                }
            }, _config.First);
            await EnterBarrierAsync("repair-4");

            await AssertCanTalk(island1.AddRange(island2).ToArray());
        });
    }

    public async Task A_Network_partition_tolerant_cluster_must_heal_after_unreachable_when_ring_is_changed()
    {
        await WithinAsync(60.Seconds(), async () =>
        {
            var joining = ImmutableArray.Create(_config.Sixth, _config.Seventh);
            var others = ImmutableArray.Create(_config.Second, _config.Third, _config.Fourth, _config.Fifth);

            await RunOnAsync(async () =>
            {
                foreach (var role1 in joining.Add(_config.First))
                {
                    foreach (var role2 in others)
                    {
                        await TestConductor.BlackholeAsync(role1, role2, ThrottleTransportAdapter.Direction.Both);
                    }
                }
            }, _config.First);
            await EnterBarrierAsync("blackhole-5");

            await RunOnAsync(async () =>
            {
                await AssertUnreachable(others.ToArray());
            }, _config.First);

            await RunOnAsync(async () =>
            {
                await AssertUnreachable(_config.First);
            }, others.ToArray());

            await EnterBarrierAsync("unreachable-5");

            await RunOnAsync(async () =>
            {
                // ReSharper disable once MethodHasAsyncOverload
                Cluster.Join(GetAddress(_config.First));

                // let them join and stabilize heartbeating
                await Task.Delay(Dilated(5000.Milliseconds()));
            }, joining.ToArray());

            await EnterBarrierAsync("joined-5");

            await RunOnAsync(async () =>
            {
                await AssertUnreachable(others.ToArray());
            }, joining.Add(_config.First).ToArray());

            // others doesn't know about the joining nodes yet, no gossip passed through
            await RunOnAsync(async () =>
            {
                await AssertUnreachable(_config.First);
            }, others.ToArray());

            await EnterBarrierAsync("more-unreachable-5");

            await RunOnAsync(async () =>
            {
                foreach (var role1 in joining.Add(_config.First))
                {
                    foreach (var role2 in others)
                    {
                        await TestConductor.PassThroughAsync(role1, role2, ThrottleTransportAdapter.Direction.Both);
                    }
                }
            }, _config.First);
            await EnterBarrierAsync("repair-5");

            await RunOnAsync(async () =>
            {
                // eighth not joined yet
                await AwaitMembersUpAsync(Roles.Count - 1, timeout: Remaining);
            }, joining.AddRange(others).Add(_config.First).ToArray());
            await EnterBarrierAsync("after-5");

            await AssertCanTalk(joining.AddRange(others).Add(_config.First).ToArray());
        });
    }

    public async Task A_Network_partition_tolerant_cluster_must_mark_quarantined_node_with_reachability_status_Terminated()
    {
        await WithinAsync(60.Seconds(), async () =>
        {
            var others = ImmutableArray.Create(_config.First, _config.Third, _config.Fourth, _config.Fifth, _config.Sixth, _config.Seventh);

            await RunOnAsync(() =>
            {
                Sys.ActorOf<SurviveNetworkInstabilitySpecConfig.Watcher>("watcher");

                // undelivered system messages in RemoteChild on third should trigger QuarantinedEvent
                Sys.EventStream.Subscribe(TestActor, typeof(QuarantinedEvent));
                return Task.CompletedTask;
            }, _config.Third);
            await EnterBarrierAsync("watcher-created");

            await RunOnAsync(async () =>
            {
                var sysMsgBufferSize = Sys
                    .AsInstanceOf<ExtendedActorSystem>().Provider
                    .AsInstanceOf<RemoteActorRefProvider>().RemoteSettings.SysMsgBufferSize;

                var refs = Vector.Fill<IActorRef>(sysMsgBufferSize + 1)(
                    () => Sys.ActorOf<SurviveNetworkInstabilitySpecConfig.Echo>()).ToImmutableHashSet();

                Sys.ActorSelection(Node(_config.Third) / "user" / "watcher").Tell(new SurviveNetworkInstabilitySpecConfig.Targets(refs));
                await ExpectMsgAsync<SurviveNetworkInstabilitySpecConfig.TargetsRegistered>();
            }, _config.Second);
            await EnterBarrierAsync("targets-registered");

            await RunOnAsync(async () =>
            {
                foreach (var role in others)
                {
                    await TestConductor.BlackholeAsync(role, _config.Second, ThrottleTransportAdapter.Direction.Both);
                }
            }, _config.First);
            await EnterBarrierAsync("blackhole-6");

            await RunOnAsync(async () =>
            {
                // this will trigger watch of targets on second, resulting in too many outstanding
                // system messages and quarantine
                Sys.ActorSelection("/user/watcher").Tell("boom");
                await WithinAsync(10.Seconds(), async () =>
                {
                    (await ExpectMsgAsync<QuarantinedEvent>()).Address.Should().Be(GetAddress(_config.Second));
                });
                Sys.EventStream.Unsubscribe(TestActor, typeof(QuarantinedEvent));
            }, _config.Third);
            await EnterBarrierAsync("quarantined");

            await RunOnAsync(async () =>
            {
                await Task.Delay(2000.Milliseconds());

                var secondUniqueAddress = Cluster.State.Members.SingleOrDefault(m => m.Address == GetAddress(_config.Second));
                secondUniqueAddress.Should().NotBeNull(because: "2nd node should stay visible");
                secondUniqueAddress?.Status.Should().Be(MemberStatus.Up, because: "2nd node should be Up");
                    
                // second should be marked with reachability status Terminated
                await AwaitAssertAsync(() => ClusterView.Reachability.Status(secondUniqueAddress?.UniqueAddress).Should().Be(Reachability.ReachabilityStatus.Terminated));
            }, others.ToArray());
            await EnterBarrierAsync("reachability-terminated");

            await RunOnAsync(() =>
            {
                Cluster.Down(GetAddress(_config.Second));
                return Task.CompletedTask;
            }, _config.Fourth);

            await RunOnAsync(async () =>
            {
                // second should be removed because of quarantine
                await AwaitAssertAsync(() => ClusterView.Members.Select(c => c.Address).Should().NotContain(GetAddress(_config.Second)));
                // and also removed from reachability table
                await AwaitAssertAsync(() => ClusterView.Reachability.AllUnreachableOrTerminated.Should().BeEmpty());
            }, others.ToArray());
            await EnterBarrierAsync("removed-after-down");

            await EnterBarrierAsync("after-6");
            await AssertCanTalk(others.ToArray());
        });
    }

    public async Task A_Network_partition_tolerant_cluster_must_continue_and_move_Joining_to_Up_after_downing_of_one_half()
    {
        await WithinAsync(60.Seconds(), async () =>
        {
            // note that second is already removed in previous step
            var side1 = ImmutableArray.Create(_config.First, _config.Third, _config.Fourth);
            var side1AfterJoin = side1.Add(_config.Eighth);
            var side2 = ImmutableArray.Create(_config.Fifth, _config.Sixth, _config.Seventh);

            await RunOnAsync(async () =>
            {
                foreach (var role1 in side1AfterJoin)
                {
                    foreach (var role2 in side2)
                    {
                        await TestConductor.BlackholeAsync(role1, role2, ThrottleTransportAdapter.Direction.Both);
                    }
                }
            }, _config.First);
            await EnterBarrierAsync("blackhole-7");

            await RunOnAsync(async () =>
            {
                await AssertUnreachable(side2.ToArray());
            }, side1.ToArray());

            await RunOnAsync(async () =>
            {
                await AssertUnreachable(side1.ToArray());
            }, side2.ToArray());

            await EnterBarrierAsync("unreachable-7");

            await RunOnAsync(() =>
            {
                Cluster.Join(GetAddress(_config.Third));
                return Task.CompletedTask;
            }, _config.Eighth);

            await RunOnAsync(() =>
            {
                foreach (var role2 in side2)
                {
                    Cluster.Down(GetAddress(role2));
                }
                return Task.CompletedTask;
            }, _config.Fourth);

            await EnterBarrierAsync("downed-7");

            await RunOnAsync(async () =>
            {
                // side2 removed
                var expected = side1AfterJoin.Select(GetAddress).ToImmutableHashSet();
                await AwaitAssertAsync(() =>
                {
                    // repeat the downing in case it was not successful, which may
                    // happen if the removal was reverted due to gossip merge
                    foreach (var role2 in side2)
                    {
                        Cluster.Down(GetAddress(role2));
                    }

                    ClusterView.Members.Select(c => c.Address).Should().BeEquivalentTo(expected);
                    ClusterView.Members.Where(m => m.Address.Equals(GetAddress(_config.Eighth))).Select(m => m.Status).FirstOrDefault().Should().Be(MemberStatus.Up);
                });
            }, side1AfterJoin.ToArray());
            await EnterBarrierAsync("side2-removed");

            await RunOnAsync(async () =>
            {
                foreach (var role1 in side1AfterJoin)
                {
                    foreach (var role2 in side2)
                    {
                        await TestConductor.PassThroughAsync(role1, role2, ThrottleTransportAdapter.Direction.Both);
                    }
                }
            }, _config.First);
            await EnterBarrierAsync("repair-7");

            // side2 should not detect side1 as reachable again
            await Task.Delay(10000);

            await RunOnAsync(() =>
            {
                var expected = side1AfterJoin.Select(GetAddress).ToImmutableHashSet();
                ClusterView.Members.Select(c => c.Address).Should().BeEquivalentTo(expected);
                return Task.CompletedTask;
            }, side1AfterJoin.ToArray());

            await RunOnAsync(async () =>
            {
                // side2 comes back but stays unreachable
                var expected = side2.AddRange(side1).Select(GetAddress).ToImmutableHashSet();
                ClusterView.Members.Select(c => c.Address).Should().BeEquivalentTo(expected);
                await AssertUnreachable(side1.ToArray());
            }, side2.ToArray());

            await EnterBarrierAsync("after-7");
            await AssertCanTalk(side1AfterJoin.ToArray());
        });
    }
}