//-----------------------------------------------------------------------
// <copyright file="JoinWithOfflineSeedNodeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Cluster.TestKit;
using Akka.Event;
using Akka.Remote.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode
{
    public class JoinWithOfflineSeedNodeConfig : MultiNodeConfig
    {
        public RoleName Seed { get; }

        public RoleName NonSeed { get; }

        public JoinWithOfflineSeedNodeConfig()
        {
            Seed = Role("seed");
            NonSeed = Role("nonseed");

            CommonConfig = DebugConfig(false).WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    /// <summary>
    /// Tests to ensure that if we have 2 seed nodes defined and 1 is offline during a <see cref="Cluster.JoinSeedNodes"/>
    /// command, the joining node will still be marked as <see cref="MemberStatus.Up"/> as long as there are no unreachable nodes
    /// within the cluster.
    /// </summary>
    public class JoinWithOfflineSeedNodeSpec : MultiNodeClusterSpec
    {
        private readonly JoinWithOfflineSeedNodeConfig _config;

        private ImmutableList<Address> SeedNodes => ImmutableList.Create(SeedUniqueAddress.Address);

        private Lazy<ActorSystem> _seedSystem;
        private Lazy<ActorSystem> _unavailableSeedSystem;

        volatile UniqueAddress _seedUniqueAddress;
        public UniqueAddress SeedUniqueAddress => _seedUniqueAddress;

        private Address _verifiedLeader;

        public JoinWithOfflineSeedNodeSpec() : this(new JoinWithOfflineSeedNodeConfig()) { }

        protected JoinWithOfflineSeedNodeSpec(JoinWithOfflineSeedNodeConfig config) : base(config, typeof(JoinWithOfflineSeedNodeSpec))
        {
            _config = config;
            _seedSystem = new Lazy<ActorSystem>(() => ActorSystem.Create(Sys.Name, Sys.Settings.Config));
            _unavailableSeedSystem = new Lazy<ActorSystem>(() => ActorSystem.Create(Sys.Name, Sys.Settings.Config));
        }

        protected override void AfterTermination()
        {
            RunOn(() =>
            {
                Shutdown(_seedSystem.Value);
            }, _config.Seed);

            RunOn(() =>
            {
                if (!_unavailableSeedSystem.Value.WhenTerminated.IsCompleted)
                    Shutdown(_unavailableSeedSystem.Value);
            }, _config.NonSeed);

            base.AfterTermination();
        }

        class LeaderSynchronizer : ReceiveActor
        {
            public LeaderSynchronizer()
            {
                Receive<string>(str => str.Equals("leader"), _ => Sender.Tell(Cluster.Get(Context.System).State.Leader));
            }
        }

        /// <summary>
        ///  Solely for debugging purposes.
        /// </summary>
        class DomainEventLogger : ReceiveActor
        {
            private ILoggingAdapter _log = Context.GetLogger();

            protected override void PreStart()
            {
                Akka.Cluster.Cluster.Get(Context.System).Subscribe(Self, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents, typeof(ClusterEvent.IClusterDomainEvent));
            }

            protected override void PostStop()
            {
                Akka.Cluster.Cluster.Get(Context.System).Unsubscribe(Self);
            }

            public DomainEventLogger()
            {
                ReceiveAny(i => _log.Debug(i.ToString()));
            }
        }

        [MultiNodeFact]
        public void JoinClusterWithOneOfflineSeedNode()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                RunOn(() =>
                {
                    Sys.ActorOf(c => c.Receive<UniqueAddress>((address, ctx) =>
                    {
                        _seedUniqueAddress = address;
                        ctx.Sender.Tell("ok");
                    }), "address-receiver");
                    Sys.ActorOf(Props.Create(() => new DomainEventLogger()), "cluster-logger");


                }, _config.NonSeed);
                EnterBarrier("addr-receiver-ready");

                RunOn(() =>
                {
                    _seedUniqueAddress = Cluster.Get(_seedSystem.Value).SelfUniqueAddress;
                    _seedSystem.Value.ActorOf(Props.Create(() => new DomainEventLogger()), "clusterLogger");
                    var probe = CreateTestProbe(_seedSystem.Value);
                    _seedSystem.Value.ActorSelection(new RootActorPath(GetAddress(_config.NonSeed)) / "user" / "address-receiver").Tell(_seedUniqueAddress, probe.Ref);
                    probe.ExpectMsg("ok", TimeSpan.FromSeconds(5));
                }, _config.Seed);
                EnterBarrier("addr-transferred");

                RunOn(() =>
                {
                    Cluster.Get(_seedSystem.Value).JoinSeedNodes(SeedNodes);
                    AwaitCondition(() => Cluster.Get(_seedSystem.Value).ReadView.IsSingletonCluster);
                    _seedSystem.Value.ActorOf(Props.Create(() => new LeaderSynchronizer()), "leader-sync");
                }, _config.Seed);
                EnterBarrier("seed-self-joined");

                RunOn(() =>
                {
                    var unreachableNodeAddress = Cluster.Get(_unavailableSeedSystem.Value).SelfAddress;
                    // terminate the unreachableSeedNode
                    Shutdown(_unavailableSeedSystem.Value, RemainingOrDefault);

                    Cluster.JoinSeedNodes(SeedNodes.Add(unreachableNodeAddress)); // append the unreachable node address
                    AwaitMembersUp(2);
                    Sys.ActorOf(Props.Create(() => new LeaderSynchronizer()), "leader-sync");
                }, _config.NonSeed);
                EnterBarrier("formed-cluster");

               
                /*
                 * Verify that both nodes agree on who the leader is
                 */

                RunOn(() =>
                {
                    var addr2 = GetAddress(_config.NonSeed);
                    var probe2 = CreateTestProbe(_seedSystem.Value);
                    AwaitAssert(() =>
                    {
                        _seedSystem.Value.ActorSelection(new RootActorPath(addr2) / "user" / "leader-sync")
                            .Tell("leader", probe2.Ref);
                        _verifiedLeader = probe2.ExpectMsg<Address>();
                        _verifiedLeader.Should().Be(Cluster.Get(_seedSystem.Value).State.Leader);
                    });


                }, _config.Seed);

                RunOn(() =>
                {
                    AwaitAssert(() =>
                    {
                        _verifiedLeader =
                            Sys.ActorSelection(new RootActorPath(SeedUniqueAddress.Address) / "user" / "leader-sync")
                                .Ask<Address>("leader")
                                .Result;
                        _verifiedLeader.Should().Be(Cluster.State.Leader);
                    });
                }, _config.NonSeed);
                EnterBarrier("verified-leader");
            });
        }
    }
}

