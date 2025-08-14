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

        protected async Task EndBarrier()
        {
            _endBarrierNumber += 1;
            await EnterBarrierAsync("after_" + _endBarrierNumber);
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
            AwaitClusterUp(roles: Roles.ToArray());
            await EndBarrier();
        }

        // ReSharper disable once InconsistentNaming
        public async Task MarkNodeAsUNREACHABLEWhenWePullTheNetwork()
        {
            // let them send at least one heartbeat to each other after the gossip convergence
            // because for new joining nodes we remove them from the failure detector when
            // receive gossip
            Thread.Sleep(Dilated(TimeSpan.FromSeconds(2)));

            await RunOnAsync(async () =>
            {
                // pull network for victim node from all nodes
                foreach (var role in AllBut(_victim.Value))
                {
                    await TestConductor.BlackholeAsync(_victim.Value, role, ThrottleTransportAdapter.Direction.Both);
                }
            }, _config.First);

            await EnterBarrierAsync("unplug_victim");

            var allButVictim = AllBut(_victim.Value).ToArray();
            await RunOnAsync(async () =>
            {
                var victimAddress = GetAddress(_victim.Value);
                allButVictim.ForEach(name => MarkNodeAsUnavailable(GetAddress(name)));
                await WithinAsync(TimeSpan.FromSeconds(30), () =>
                {
                    // victim becomes all alone
                    AwaitAssert(() =>
                    {
                        var members = ClusterView.Members; // to snapshot the object
                        Assert.Equal(Roles.Count - 1, ClusterView.UnreachableMembers.Count);
                    });
                    var addresses = allButVictim.Select(GetAddress).ToList();
                    Assert.True(ClusterView.UnreachableMembers.Select(x => x.Address).All(y => addresses.Contains(y)));
                    return Task.CompletedTask;
                });
            }, _victim.Value);

            await RunOnAsync(async () =>
            {
                MarkNodeAsUnavailable(GetAddress(_victim.Value));
                await WithinAsync(TimeSpan.FromSeconds(30), () =>
                {
                    // victim becomes unreachable
                    AwaitAssert(() =>
                    {
                        var members = ClusterView.Members; // to snapshot the object
                        Assert.Single(ClusterView.UnreachableMembers);
                    });
                    AwaitSeenSameState(allButVictim.Select(GetAddress).ToArray());

                    // still once unreachable
                    Assert.Single(ClusterView.UnreachableMembers);
                    Assert.Equal(Node(_victim.Value).Address, ClusterView.UnreachableMembers.First().Address);
                    Assert.Equal(MemberStatus.Up, ClusterView.UnreachableMembers.First().Status);
                    return Task.CompletedTask;
                });
            }, allButVictim);

            await EndBarrier();
        }

        // ReSharper disable once InconsistentNaming
        public async Task MarkTheNodeAsDOWN()
        {
            RunOn(() =>
            {
                Cluster.Down(GetAddress(_victim.Value));
            }, _master.Value);

            var allButVictim = AllBut(_victim.Value, Roles).ToArray();
            RunOn(() =>
            {
                // eventually removed
                AwaitMembersUp(Roles.Count - 1, ImmutableHashSet.Create(GetAddress(_victim.Value)));
                AwaitAssert(() => Assert.True(ClusterView.UnreachableMembers.IsEmpty), TimeSpan.FromSeconds(15));
                var addresses = allButVictim.Select(GetAddress).ToList();
                AwaitAssert(() => Assert.True(ClusterView.Members.Select(x => x.Address).All(y => addresses.Contains(y))));
            }, allButVictim);

            await EndBarrier();
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
                    await TestConductor.PassThroughAsync(_victim.Value, role, ThrottleTransportAdapter.Direction.Both);
                }
            }, _config.First);

            await EnterBarrierAsync("plug_in_victim");

            await RunOnAsync(async () =>
            {
                // will shutdown ActorSystem of victim
                await TestConductor.ShutdownAsync(_victim.Value);
            }, _config.First);

            RunOn(() =>
            {
                var victimAddress = Sys.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress;
                Sys.WhenTerminated.Wait(TimeSpan.FromSeconds(10));

                // create new ActorSystem with same host:port
                var freshSystem = ActorSystem.Create(Sys.Name, ConfigurationFactory.ParseString(@"akka.remote.dot-netty.tcp{
                    hostname = "+ victimAddress.Host + @"
                    port = "+ victimAddress.Port + @"
                }").WithFallback(Sys.Settings.Config));

                try
                {
                    Cluster.Get(freshSystem).Join(masterAddress);
                    Within(TimeSpan.FromSeconds(15), () =>
                    {
                        AwaitAssert(() => Assert.Contains(victimAddress, Cluster.Get(freshSystem).State.Members.Select(x => x.Address)));
                        AwaitAssert(() => Assert.Equal(expectedNumberOfMembers,Cluster.Get(freshSystem).State.Members.Count));
                        AwaitAssert(() => Assert.True(Cluster.Get(freshSystem).State.Members.All(y => y.Status == MemberStatus.Up)));
                    });

                    // signal to master node that victim is done
                    var endProbe = CreateTestProbe(freshSystem);
                    var endActor = freshSystem.ActorOf(Props.Create(() => new EndActor(endProbe.Ref, masterAddress)),
                        "end");
                    endActor.Tell(EndActor.SendEnd.Instance);
                    endProbe.ExpectMsg<EndActor.EndAck>();
                }
                finally
                {
                    Shutdown(freshSystem);
                }
                // no barrier here, because it is not part of testConductor roles any more
            }, _victim.Value);

            RunOn(() =>
            {
                AwaitMembersUp(expectedNumberOfMembers);
                // don't end the test until the freshSystem is done
                RunOn(() =>
                {
                    ExpectMsg<EndActor.End>(TimeSpan.FromSeconds(20));
                }, _master.Value);
                EndBarrier();
            }, AllBut(_victim.Value).ToArray());
        }
    }
}
