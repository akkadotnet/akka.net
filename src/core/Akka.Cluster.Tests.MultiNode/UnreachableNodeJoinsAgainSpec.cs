//-----------------------------------------------------------------------
// <copyright file="UnreachableNodeJoinsAgainSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Cluster.Tests.MultiNode
{
    public class UnreachableNodeJoinsAgainConfig : MultiNodeConfig
    {
        public RoleName First { get; }

        public RoleName Second { get; }

        public RoleName Third { get; }

        public RoleName Fourth { get; }

        public UnreachableNodeJoinsAgainConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");
            // The master's end-of-spec path is now its 20s wait for End followed by up to 25s waiting
            // for the victim to go unreachable, 45s back to back, while first and third are already
            // parked on the final barrier. The conductor arms the barrier's clock at the FIRST arrival
            // and never extends it, so the default 30s barrier could expire before the master's own
            // assertion reports. 60s is the value InitialHeartbeatSpec and six other multi-node specs
            // use for the same reason.
            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.remote.log-remote-lifecycle-events = off
                akka.testconductor.barrier-timeout = 60s")
                .WithFallback(DebugConfig(false)).WithFallback(MultiNodeClusterSpec.ClusterConfig());
            TestTransport = true; // need to use the throttler and blackhole
        }
    }

    public class UnreachableNodeJoinsAgainSpec : MultiNodeClusterSpec
    {
        private readonly UnreachableNodeJoinsAgainConfig _config;

        Lazy<RoleName> _master;
        Lazy<RoleName> _victim;

        private int _endBarrierNumber = 0;

        public UnreachableNodeJoinsAgainSpec () : this(new UnreachableNodeJoinsAgainConfig()){ }

        protected UnreachableNodeJoinsAgainSpec(UnreachableNodeJoinsAgainConfig config) : base(config, typeof(UnreachableNodeJoinsAgainSpec))
        {
            _config = config;
            _master = new Lazy<RoleName>(() => _config.Second);
            _victim = new Lazy<RoleName>(() => _config.Fourth);
            MuteMarkingAsUnreachable();
        }

        protected IEnumerable<RoleName> AllBut(RoleName roleName)
        {
            return AllBut(roleName, Roles);
        }

        protected IEnumerable<RoleName> AllBut(RoleName roleName, IEnumerable<RoleName> roles)
        {
            return roles.Where(x => !x.Equals(roleName));
        }

        protected Task EndBarrierAsync()
        {
            _endBarrierNumber += 1;
            return EnterBarrierAsync("after_" + _endBarrierNumber);
        }

        [MultiNodeFact]
        public async Task AClusterOf4MembersMust()
        {
            await ReachInitialConvergence();
            await MarkNodeAsUNREACHABLEWhenWePullTheNetwork();
            await MarkTheNodeAsDOWN();
            await AllowFreshNodeWithSameHostAndPortToJoinAgainWhenTheNetworkIsPluggedBackIn();
        }

        public async Task ReachInitialConvergence()
        {
            await AwaitClusterUpAsync(Roles.ToArray());
            await EndBarrierAsync();
        }

        // ReSharper disable once InconsistentNaming
        public async Task MarkNodeAsUNREACHABLEWhenWePullTheNetwork()
        {
            // Wait until this node's failure detector has seen a heartbeat from every peer.
            // A joining node is dropped from the failure detector when gossip arrives, so the
            // detector has to be warm before we pull the network - otherwise the victim is
            // never marked unreachable. Every node monitors every other node here, because
            // akka.cluster.monitored-by-nr-of-members defaults to 9 and this cluster has 4.
            var peers = AllBut(Myself).Select(GetAddress).ToArray();
            await AwaitAssertAsync(() =>
            {
                foreach (var peer in peers)
                {
                    Assert.True(Cluster.FailureDetector.IsMonitoring(peer),
                        $"Failure detector on [{Cluster.SelfAddress}] is not monitoring [{peer}] yet");
                }
            }, TimeSpan.FromSeconds(20));

            await RunOnAsync(async () =>
            {
                // pull network for victim node from all nodes
                foreach (var role in AllBut(_victim.Value))
                {
                    await TestConductor.Blackhole(_victim.Value, role, ThrottleTransportAdapter.Direction.Both);
                }
            }, _config.First);

            await EnterBarrierAsync("unplug_victim");

            var allButVictim = AllBut(_victim.Value).ToArray();
            await RunOnAsync(async () =>
            {
                allButVictim.ForEach(name => MarkNodeAsUnavailable(GetAddress(name)));
                var expectedUnreachable = allButVictim.Select(GetAddress).ToImmutableHashSet();
                await WithinAsync(TimeSpan.FromSeconds(30), async () =>
                {
                    // Victim becomes all alone. Snapshot the unreachable set once so the count
                    // and the address check describe the same cluster view.
                    await AwaitAssertAsync(() =>
                    {
                        var unreachable = ClusterView.UnreachableMembers;
                        Assert.Equal(Roles.Count - 1, unreachable.Count);
                        Assert.True(unreachable.Select(x => x.Address).All(expectedUnreachable.Contains),
                            "victim should see every other node as unreachable");
                    });
                });
            }, _victim.Value);

            await RunOnAsync(async () =>
            {
                MarkNodeAsUnavailable(GetAddress(_victim.Value));
                var victimNodeAddress = GetAddress(_victim.Value);
                await WithinAsync(TimeSpan.FromSeconds(30), async () =>
                {
                    // victim becomes unreachable
                    await AwaitAssertAsync(() => Assert.Single(ClusterView.UnreachableMembers));
                    await AwaitSeenSameStateAsync(CancellationToken.None, allButVictim.Select(GetAddress).ToArray());

                    // Still exactly one unreachable member, and it is the victim. Read the set
                    // once and assert everything off that snapshot - gossip can move between
                    // separate reads of the live ClusterView.
                    await AwaitAssertAsync(() =>
                    {
                        var unreachable = ClusterView.UnreachableMembers;
                        Assert.Single(unreachable);
                        var victimMember = unreachable.First();
                        Assert.Equal(victimNodeAddress, victimMember.Address);
                        Assert.Equal(MemberStatus.Up, victimMember.Status);
                    });
                });
            }, allButVictim);

            await EndBarrierAsync();
        }

        // ReSharper disable once InconsistentNaming
        public async Task MarkTheNodeAsDOWN()
        {
            await RunOnAsync(() =>
            {
                Cluster.Down(GetAddress(_victim.Value));
                return Task.CompletedTask;
            }, _master.Value);

            var allButVictim = AllBut(_victim.Value, Roles).ToArray();
            await RunOnAsync(async () =>
            {
                // eventually removed
                await AwaitMembersUpAsync(Roles.Count - 1, ImmutableHashSet.Create(GetAddress(_victim.Value)));
                await AwaitAssertAsync(() => Assert.True(ClusterView.UnreachableMembers.IsEmpty), TimeSpan.FromSeconds(15));
                var addresses = allButVictim.Select(GetAddress).ToImmutableHashSet();
                await AwaitAssertAsync(() => Assert.True(ClusterView.Members.Select(x => x.Address).All(addresses.Contains)));
            }, allButVictim);

            await EndBarrierAsync();
        }

        public async Task AllowFreshNodeWithSameHostAndPortToJoinAgainWhenTheNetworkIsPluggedBackIn()
        {
            var expectedNumberOfMembers = Roles.Count;

            // victim actor system will be shutdown, not part of TestConductor any more
            // so we can't use barriers to synchronize with it
            var masterAddress = GetAddress(_master.Value);
            await RunOnAsync(() =>
            {
                Sys.ActorOf(Props.Create(() => new EndActor(TestActor, null)), "end");
                return Task.CompletedTask;
            }, _master.Value);
            await EnterBarrierAsync("end-actor-created");

            await RunOnAsync(async () =>
            {
                // put the network back in
                foreach (var role in AllBut(_victim.Value))
                {
                    await TestConductor.PassThrough(_victim.Value, role, ThrottleTransportAdapter.Direction.Both);
                }
            }, _config.First);

            await EnterBarrierAsync("plug_in_victim");

            await RunOnAsync(async () =>
            {
                // will shutdown ActorSystem of victim
                await TestConductor.ShutdownAsync(_victim.Value);
            }, _config.First);

            await RunOnAsync(async () =>
            {
                var victimAddress = Sys.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress;

                // The fresh system below rebinds this exact host:port, so the old system has to
                // release it first. Assert the wait instead of discarding it, otherwise a failed
                // termination surfaces later as a confusing bind error on the fresh system.
                var terminationTimeout = TimeSpan.FromSeconds(10);
                try
                {
                    await Sys.WhenTerminated.WaitAsync(terminationTimeout);
                }
                catch (TimeoutException)
                {
                    Assert.Fail($"Failed to stop [{Sys.Name}] within [{terminationTimeout}]. " +
                                $"The fresh system cannot rebind [{victimAddress}] until the old one releases it.");
                }

                // create new ActorSystem with same host:port
                // Pin the fresh system to the SAME wire address for BOTH transports - under
                // AKKA_MNTR_TRANSPORT=artery the classic dot-netty key is inert and the fresh
                // system would bind a random artery canonical.port instead.
                var freshSystem = ActorSystem.Create(Sys.Name, ConfigurationFactory.ParseString(@"akka.remote.dot-netty.tcp{
                    hostname = "+ victimAddress.Host + @"
                    port = "+ victimAddress.Port + @"
                }
                akka.remote.artery.canonical.hostname = "+ victimAddress.Host + @"
                akka.remote.artery.canonical.port = "+ victimAddress.Port + @"
                ").WithFallback(Sys.Settings.Config));

                try
                {
                    Cluster.Get(freshSystem).Join(masterAddress);

                    // This spec's own Sys is terminated by now, so its TestKit scheduler is dead
                    // and cannot drive an await loop. Run the wait from a probe attached to the
                    // live fresh system, and snapshot the member set once so all three checks
                    // describe the same view.
                    var freshProbe = CreateTestProbe(freshSystem);
                    await freshProbe.AwaitAssertAsync(() =>
                    {
                        var members = Cluster.Get(freshSystem).State.Members;
                        Assert.Contains(victimAddress, members.Select(x => x.Address));
                        Assert.Equal(expectedNumberOfMembers, members.Count);
                        Assert.True(members.All(y => y.Status == MemberStatus.Up),
                            "all members should be Up once the fresh node has rejoined");
                    }, TimeSpan.FromSeconds(25));

                    // Signal to master node that victim is done.
                    // Resolve the master's end actor first. The Identify round trip proves the
                    // association to the just-rebound address carries traffic in both directions
                    // before the handshake depends on it, and a failure names that problem
                    // instead of showing up as a missing EndAck.
                    var endProbe = CreateTestProbe(freshSystem);
                    // ResolveOne throws (rather than returning null) if the actor can't be found,
                    // so there is nothing left to assert here.
                    await freshSystem
                        .ActorSelection(new RootActorPath(masterAddress) / "user" / "end")
                        .ResolveOne(Dilated(TimeSpan.FromSeconds(20)));

                    var endActor = freshSystem.ActorOf(Props.Create(() => new EndActor(endProbe.Ref, masterAddress)),
                        "end");
                    endActor.Tell(EndActor.SendEnd.Instance);

                    // The master now stays alive until it sees this system go unreachable (below),
                    // so the ack is written by a provably live peer over a lane the resolve above
                    // just proved hot - the sub-second path measured for this handshake, not the
                    // ~30s a dead master would need. 10s is roughly 1000x that measured cost and
                    // comfortably below the master's 25s wait, so a genuinely lost ack is reported
                    // here, by the victim, with a message that names the problem - instead of the
                    // master timing out first with a message that names nothing.
                    await endProbe.ExpectMsgAsync<EndActor.EndAck>(TimeSpan.FromSeconds(10));
                }
                finally
                {
                    await ShutdownAsync(freshSystem, TimeSpan.FromSeconds(5));
                }
                // no barrier here, because it is not part of testConductor roles any more
            }, _victim.Value);

            await RunOnAsync(async () =>
            {
                await AwaitMembersUpAsync(expectedNumberOfMembers);
                // don't end the test until the freshSystem is done
                await RunOnAsync(async () =>
                {
                    await ExpectMsgAsync<EndActor.End>(TimeSpan.FromSeconds(20));

                    // Receiving End is not proof the victim is done - the EndAck this node just
                    // emitted, in the same OnReceive that released the ExpectMsg above, still has
                    // to reach it, and that ack is an ordinary, at-most-once Artery message. If we
                    // return now, one already-parked barrier and xunit's teardown put this node
                    // into ActorSystem.Terminate() within tens of milliseconds, /user stops, and
                    // the outbound stream dies with it before any shutdown flush can run - so the
                    // ack can be lost with no trace (no Dropped, no dead letter, nothing).
                    //
                    // Wait instead for the event that proves the victim no longer needs us: its
                    // departure. The fresh system shuts itself down as soon as its
                    // ExpectMsg<EndAck> returns OR throws, so "victim unreachable" is causally
                    // downstream of the ack either landing or being given up on - this node
                    // cannot begin terminating before that has happened. On the run that exposed
                    // this race, the failure detector marked the victim unreachable 3.5-4.5s after
                    // it terminated (heartbeat-interval/reaper 500ms each, MultiNodeClusterSpec.cs
                    // :53/:56); 25s undilated is comfortably above that with headroom for the
                    // victim's own 10s ack wait plus its 5s shutdown.
                    //
                    // Match on address, not uid: downing-provider-class is empty for multi-node
                    // specs, so this member is never removed and Members never changes - only
                    // reachability does. Waiting on Members would hang forever.
                    var victimAddress = GetAddress(_victim.Value);
                    await AwaitAssertAsync(() =>
                    {
                        var unreachable = ClusterView.UnreachableMembers.Select(m => m.Address).ToImmutableHashSet();
                        Assert.True(unreachable.Contains(victimAddress),
                            $"[{victimAddress}] should have become unreachable once the fresh victim " +
                            $"system terminated; currently unreachable: [{string.Join(", ", unreachable)}]");
                    }, TimeSpan.FromSeconds(25), TimeSpan.FromMilliseconds(250));
                }, _master.Value);
                await EndBarrierAsync();
            }, AllBut(_victim.Value).ToArray());
        }
    }
}
