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
            CommonConfig = ConfigurationFactory.ParseString("akka.remote.log-remote-lifecycle-events = off")
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
                var victimNodeAddress = Node(_victim.Value).Address;
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
                await TestConductor.Shutdown(_victim.Value);
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
                    var masterEndActor = await freshSystem
                        .ActorSelection(new RootActorPath(masterAddress) / "user" / "end")
                        .ResolveOne(Dilated(TimeSpan.FromSeconds(20)));
                    Assert.NotNull(masterEndActor);

                    var endActor = freshSystem.ActorOf(Props.Create(() => new EndActor(endProbe.Ref, masterAddress)),
                        "end");
                    endActor.Tell(EndActor.SendEnd.Instance);

                    // The master waits up to 20s for End, so the victim has to wait longer than
                    // that for the EndAck. The old code inherited the 15s single-expect default,
                    // which was the smallest budget in the spec and guarded the step needing the
                    // most time.
                    await endProbe.ExpectMsgAsync<EndActor.EndAck>(TimeSpan.FromSeconds(30));
                }
                finally
                {
                    Shutdown(freshSystem);
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
                }, _master.Value);
                await EndBarrierAsync();
            }, AllBut(_victim.Value).ToArray());
        }
    }
}
