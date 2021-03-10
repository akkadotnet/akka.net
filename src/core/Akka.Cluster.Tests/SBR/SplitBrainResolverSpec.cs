//-----------------------------------------------------------------------
// <copyright file="SplitBrainResolverSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Coordination;
using Akka.TestKit;
using Xunit.Abstractions;
using Akka.Util.Internal;
using Xunit;
using Akka.Cluster.SBR;
using System.Collections.Immutable;
using FluentAssertions;
using Akka.Configuration;
using Akka.Cluster.Tools.Tests;
using Akka.Remote;
using System.Text.RegularExpressions;
using System.Threading;
using Akka.Util;

namespace Akka.Cluster.Tests.SBR
{
    public class SplitBrainResolverSpec : AkkaSpec
    {
        internal class DownCalled : IEquatable<DownCalled>
        {
            public DownCalled(Address address)
            {
                Address = address;
            }

            public Address Address { get; }

            public bool Equals(DownCalled other)
            {
                if (ReferenceEquals(other, null))
                    return false;
                return Address.Equals(other.Address);
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as DownCalled);
            }

            public override int GetHashCode()
            {
                return Address.GetHashCode();
            }
        }

        internal class DowningTestActor : SplitBrainResolverBase
        {
            public static Props Props(
                TimeSpan stableAfter,
                DowningStrategy strategy,
                IActorRef probe,
                UniqueAddress selfUniqueAddress,
                TimeSpan downAllWhenUnstable,
                TimeSpan tick)
            {
                return Actor.Props.Create(() => new DowningTestActor(
                    stableAfter,
                    strategy,
                    probe,
                    selfUniqueAddress,
                    downAllWhenUnstable,
                    tick));
            }

            public DowningTestActor(
                TimeSpan stableAfter,
                DowningStrategy strategy,
                IActorRef probe,
                UniqueAddress selfUniqueAddress,
                TimeSpan downAllWhenUnstable,
                TimeSpan tick)
                : base(stableAfter, strategy)
            {
                Probe = probe;
                SelfUniqueAddress = selfUniqueAddress;
                DownAllWhenUnstable = downAllWhenUnstable;
                Tick = tick;
            }

            public IActorRef Probe { get; }

            public new TimeSpan Tick { get; }

            public override UniqueAddress SelfUniqueAddress { get; }
            public override TimeSpan DownAllWhenUnstable { get; }

            // manual ticks used in this test
            public override TimeSpan TickInterval => Tick == TimeSpan.Zero ? base.TickInterval : Tick;

            // immediate overdue if Duration.Zero is used
            protected override Deadline NewStableDeadline()
            {
                return base.NewStableDeadline() + TimeSpan.FromMilliseconds(-1);
            }

            ImmutableHashSet<Address> downed = ImmutableHashSet<Address>.Empty;

            public override void Down(UniqueAddress node, IDecision decision)
            {
                if (Leader && !downed.Contains(node.Address))
                {
                    downed = downed.Add(node.Address);
                    Probe.Tell(new DownCalled(node.Address));
                }
                else if (!Leader)
                    Probe.Tell("down must only be done by leader");
            }

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case ClusterEvent.UnreachableMember m when Strategy.Unreachable.Contains(m.Member.UniqueAddress):
                        // already unreachable
                        return true;
                    case ClusterEvent.ReachableMember m when !Strategy.Unreachable.Contains(m.Member.UniqueAddress):
                        // already reachable
                        return true;
                }
                return base.Receive(message);
            }
        }

        const string Config = @"
            akka {
                actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                cluster.downing-provider-class = ""Akka.Cluster.SBR.SplitBrainResolverProvider""
                cluster.split-brain-resolver.active-strategy=keep-majority
                remote.dot-netty.tcp.hostname = 127.0.0.1
                remote.dot-netty.tcp.port = 0
            }";

        private LeaseSettings testLeaseSettings;

        public SplitBrainResolverSpec(ITestOutputHelper output)
            : base(Config, output)
        {
            testLeaseSettings = new LeaseSettings("akka-sbr", "test", new TimeoutSettings(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(3)), ConfigurationFactory.Empty);

            #region TestAddresses

            var addressA = new Address("akka.tcp", "sys", "a", 2552);
            MemberA =
                new Member(
                    new UniqueAddress(addressA, 0),
                    5,
                    MemberStatus.Up,
                    new[] { "role3" },
                    AppVersion.Zero);
            MemberB =
                new Member(
                      new UniqueAddress(new Address(addressA.Protocol, addressA.System, "b", addressA.Port), 0),
                      4,
                      MemberStatus.Up,
                      new[] { "role1", "role3" },
                      AppVersion.Zero);
            MemberC =
                new Member(
                      new UniqueAddress(new Address(addressA.Protocol, addressA.System, "c", addressA.Port), 0),
                      3,
                      MemberStatus.Up,
                      new[] { "role2" },
                      AppVersion.Zero);
            MemberD =
                new Member(
                      new UniqueAddress(new Address(addressA.Protocol, addressA.System, "d", addressA.Port), 0),
                      2,
                      MemberStatus.Up,
                      new[] { "role1", "role2", "role3" },
                      AppVersion.Zero);
            MemberE =
                new Member(
                      new UniqueAddress(new Address(addressA.Protocol, addressA.System, "e", addressA.Port), 0),
                      1,
                      MemberStatus.Up,
                      new string[] { },
                      AppVersion.Zero);
            MemberF =
                new Member(
                      new UniqueAddress(new Address(addressA.Protocol, addressA.System, "f", addressA.Port), 0),
                      5,
                      MemberStatus.Up,
                      new string[] { },
                      AppVersion.Zero);
            MemberG =
                new Member(
                      new UniqueAddress(new Address(addressA.Protocol, addressA.System, "g", addressA.Port), 0),
                      6,
                      MemberStatus.Up,
                      new string[] { },
                      AppVersion.Zero);

            MemberAWeaklyUp = new Member(MemberA.UniqueAddress, int.MaxValue, MemberStatus.WeaklyUp, MemberA.Roles, AppVersion.Zero);
            MemberBWeaklyUp = new Member(MemberB.UniqueAddress, int.MaxValue, MemberStatus.WeaklyUp, MemberB.Roles, AppVersion.Zero);

            #endregion TestAddresses
        }

        #region TestAddresses

        public Member MemberA { get; }
        public Member MemberB { get; }
        public Member MemberC { get; }
        public Member MemberD { get; }
        public Member MemberE { get; }
        public Member MemberF { get; }
        public Member MemberG { get; }

        public Member MemberAWeaklyUp { get; }
        public Member MemberBWeaklyUp { get; }

        public Member Joining(Member m) => Member.Create(m.UniqueAddress, m.Roles, AppVersion.Zero);

        public Member Leaving(Member m) => m.Copy(MemberStatus.Leaving);

        public Member Exiting(Member m) => Leaving(m).Copy(MemberStatus.Exiting);

        public Member Downed(Member m) => m.Copy(MemberStatus.Down);

        #endregion TestAddresses

        internal Reachability CreateReachability(IEnumerable<(Member, Member)> unreachability)
        {
            return new Reachability(unreachability
                .Select(i => new Reachability.Record(i.Item1.UniqueAddress, i.Item2.UniqueAddress, Reachability.ReachabilityStatus.Unreachable, 1)).ToImmutableList(),
                unreachability.ToImmutableDictionary(i => i.Item1.UniqueAddress, _ => 1L)
                );
        }

        public ExtendedActorSystem ExtSystem => Sys.AsInstanceOf<ExtendedActorSystem>();

        internal abstract class StrategySetup
        {
            private readonly SplitBrainResolverSpec owner;

            public StrategySetup(SplitBrainResolverSpec owner)
            {
                this.owner = owner;
            }

            public abstract DowningStrategy CreateStrategy();

            public ImmutableHashSet<Member> Side1 { get; set; } = ImmutableHashSet<Member>.Empty;
            public ImmutableHashSet<Member> Side2 { get; set; } = ImmutableHashSet<Member>.Empty;
            public ImmutableHashSet<Member> Side3 { get; set; } = ImmutableHashSet<Member>.Empty;

            public ImmutableHashSet<UniqueAddress> Side1Nodes => Side1.Select(m => m.UniqueAddress).ToImmutableHashSet();
            public ImmutableHashSet<UniqueAddress> Side2Nodes => Side2.Select(m => m.UniqueAddress).ToImmutableHashSet();
            public ImmutableHashSet<UniqueAddress> Side3Nodes => Side3.Select(m => m.UniqueAddress).ToImmutableHashSet();

            public ImmutableHashSet<(Member, Member)> IndirectlyConnected = ImmutableHashSet<(Member, Member)>.Empty;

            private DowningStrategy InitStrategy()
            {
                var strategy = CreateStrategy();
                foreach (var m in Side1.Union(Side2).Union(Side3))
                    strategy.Add(m);
                return strategy;
            }

            public void AssertDowning(IEnumerable<Member> members)
            {
                AssertDowningSide(Side1, members);
                AssertDowningSide(Side2, members);
                AssertDowningSide(Side3, members);
            }

            public void AssertDowningSide(ImmutableHashSet<Member> side, IEnumerable<Member> members)
            {
                if (!side.IsEmpty)
                    Strategy(side).NodesToDown().Should().BeEquivalentTo(members.Select(i => i.UniqueAddress));
            }

            public DowningStrategy Strategy(ImmutableHashSet<Member> side)
            {
                var others = Side1.Union(Side2).Union(Side3).Except(side);
                (side.Except(others)).Should().BeEquivalentTo(side);

                if (!side.IsEmpty)
                {
                    var strategy = InitStrategy();
                    var unreachability = IndirectlyConnected.Union(others.Select(o => (side.FirstOrDefault(), o))).ToImmutableHashSet().ToList();
                    var r = owner.CreateReachability(unreachability);
                    strategy.SetReachability(r);

                    foreach (var i in unreachability)
                        strategy.AddUnreachable(i.Item2);

                    strategy.SetSeenBy(side.Select(i => i.Address).ToImmutableHashSet());
                    return strategy;
                }
                else
                    return CreateStrategy();
            }
        }

        internal class StaticQuorumSetup : StrategySetup
        {
            public StaticQuorumSetup(SplitBrainResolverSpec owner, int size, string role)
                : base(owner)
            {
                Size = size;
                Role = role;
            }

            public int Size { get; }
            public string Role { get; }

            public override DowningStrategy CreateStrategy()
            {
                return new Akka.Cluster.SBR.StaticQuorum(Size, Role);
            }
        }

        [Fact]
        public void StaticQuorum_must_down_unreachable_when_enough_reachable_nodes_in_Setup2_3_None()
        {
            var setup = new StaticQuorumSetup(this, 3, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberC, MemberE);
            setup.Side2 = ImmutableHashSet.Create(MemberB, MemberD);
            setup.AssertDowning(setup.Side2);
        }

        [Fact]
        public void StaticQuorum_must_down_reachable_when_not_enough_reachable_nodes()
        {
            var setup = new StaticQuorumSetup(this, 3, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB);
            setup.Side2 = ImmutableHashSet.Create(MemberC, MemberD);
            setup.Strategy(setup.Side1).Decide().Should().BeOfType<DownReachable>();
            setup.Strategy(setup.Side2).Decide().Should().BeOfType<DownReachable>();
        }

        [Fact]
        public void StaticQuorum_must_down_unreachable_when_enough_reachable_nodes_with_role()
        {
            var setup = new StaticQuorumSetup(this, 2, "role3");
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberC);
            setup.Side2 = ImmutableHashSet.Create(MemberD, MemberE);
            setup.AssertDowning(setup.Side2);
        }

        [Fact]
        public void StaticQuorum_must_down_all_if_N_gt_static_quorum_size_x_2_sub_1()
        {
            var setup = new StaticQuorumSetup(this, 3, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberC);
            setup.Side2 = ImmutableHashSet.Create(MemberD, MemberE, MemberF);
            setup.AssertDowning(setup.Side1.Union(setup.Side2));
        }

        [Fact]
        public void StaticQuorum_must_handle_joining()
        {
            var setup = new StaticQuorumSetup(this, 3, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, Joining(MemberC));
            setup.Side2 = ImmutableHashSet.Create(MemberD, MemberE, Joining(MemberF));

            // Joining not counted
            setup.Strategy(setup.Side1).Decide().Should().BeOfType<DownReachable>();
            setup.Strategy(setup.Side2).Decide().Should().BeOfType<DownReachable>();

            // if C becomes Up
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberC);
            setup.Strategy(setup.Side1).Decide().Should().BeOfType<DownUnreachable>();
            setup.Strategy(setup.Side2).Decide().Should().BeOfType<DownReachable>();

            // if F becomes Up, C still Joining
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, Joining(MemberC));
            setup.Side2 = ImmutableHashSet.Create(MemberD, MemberE, MemberF);
            setup.Strategy(setup.Side1).Decide().Should().BeOfType<DownReachable>();
            setup.Strategy(setup.Side2).Decide().Should().BeOfType<DownUnreachable>();

            // if both C and F become Up, too many
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberC);
            setup.Side2 = ImmutableHashSet.Create(MemberD, MemberE, MemberF);
            setup.Strategy(setup.Side1).Decide().Should().BeOfType<DownAll>();
            setup.Strategy(setup.Side2).Decide().Should().BeOfType<DownAll>();
        }

        [Fact]
        public void StaticQuorum_must_handle_leaving_exiting()
        {
            var setup = new StaticQuorumSetup(this, 3, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, Leaving(MemberC));
            setup.Side2 = ImmutableHashSet.Create(MemberD, MemberE);

            setup.Strategy(setup.Side1).Decide().Should().BeOfType<DownUnreachable>();
            setup.Strategy(setup.Side2).Decide().Should().BeOfType<DownReachable>();

            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, Exiting(MemberC));
            setup.Strategy(setup.Side1).Decide().Should().BeOfType<DownReachable>();
            setup.Strategy(setup.Side2).Decide().Should().BeOfType<DownReachable>();
        }

        internal class KeepMajoritySetup : StrategySetup
        {
            public KeepMajoritySetup(SplitBrainResolverSpec owner, string role = null)
                : base(owner)
            {
                Role = role;
            }

            public string Role { get; }

            public override DowningStrategy CreateStrategy()
            {
                return new Akka.Cluster.SBR.KeepMajority(Role);
            }
        }

        [Fact]
        public void KeepMajority_must_down_minority_partition_A_C_E__B_D___A_C_E()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberC, MemberE);
            setup.Side2 = ImmutableHashSet.Create(MemberB, MemberD);
            setup.AssertDowning(setup.Side2);
        }

        [Fact]
        public void KeepMajority_must_down_minority_partition_A_B__C_D_E___C_D_E()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB);
            setup.Side2 = ImmutableHashSet.Create(MemberC, MemberD, MemberE);
            setup.AssertDowning(setup.Side1);
        }

        [Fact]
        public void KeepMajority_must_down_self_when_alone_B__A_C___A_C()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberB);
            setup.Side2 = ImmutableHashSet.Create(MemberA, MemberC);
            setup.AssertDowning(setup.Side1);
        }

        [Fact]
        public void KeepMajority_must_keep_half_with_lowest_address_when_equal_size_partition_A_B__C_D___A_B()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB);
            setup.Side2 = ImmutableHashSet.Create(MemberC, MemberD);
            setup.AssertDowning(setup.Side2);
        }

        [Fact]
        public void KeepMajority_must_keep_node_with_lowest_address_in_two_node_cluster_A__B___A()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA);
            setup.Side2 = ImmutableHashSet.Create(MemberB);
            setup.AssertDowning(setup.Side2);
        }

        [Fact]
        public void KeepMajority_must_down_minority_partition_with_role_A_B__C_D_E___A_B()
        {
            var setup = new KeepMajoritySetup(this, "role3");
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB);
            setup.Side2 = ImmutableHashSet.Create(MemberC, MemberD, MemberE);
            setup.AssertDowning(setup.Side2);
        }

        [Fact]
        public void KeepMajority_must_keep_half_with_lowest_address_with_role_when_equal_size_partition_A_D_E__B_C___B_C()
        {
            var setup = new KeepMajoritySetup(this, "role2");
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberD, MemberE);
            setup.Side2 = ImmutableHashSet.Create(MemberB, MemberC);
            // memberC is lowest with role2
            setup.AssertDowning(setup.Side1);
        }

        [Fact]
        public void KeepMajority_must_down_all_when_no_node_with_role_C__E___()
        {
            var setup = new KeepMajoritySetup(this, "role3");
            setup.Side1 = ImmutableHashSet.Create(MemberC);
            setup.Side2 = ImmutableHashSet.Create(MemberE);
            setup.AssertDowning(setup.Side1.Union(setup.Side2));
        }

        [Fact]
        public void KeepMajority_must_not_count_joining_node_but_down_it_B_D__Aj_C___B_D()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberB, MemberD);
            setup.Side2 = ImmutableHashSet.Create(Joining(MemberA), MemberC);
            setup.AssertDowning(setup.Side2);
        }

        [Fact]
        public void KeepMajority_must_down_minority_partition_and_joining_node_A_Bj__C_D_E___C_D_E()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, Joining(MemberB));
            setup.Side2 = ImmutableHashSet.Create(MemberC, MemberD, MemberE);
            setup.AssertDowning(setup.Side1);
        }

        [Fact]
        public void KeepMajority_must_down_each_part_when_split_in_3_too_small_parts_A_B__C_D__E___()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB);
            setup.Side2 = ImmutableHashSet.Create(MemberC, MemberD);
            setup.Side3 = ImmutableHashSet.Create(MemberE);
            setup.AssertDowningSide(setup.Side1, setup.Side1);
            setup.AssertDowningSide(setup.Side2, setup.Side2);
            setup.AssertDowningSide(setup.Side3, setup.Side3);
        }

        [Fact]
        public void KeepMajority_must_detect_edge_case_of_membership_change_A_B_F_G__C_D_E___A_B_F_G()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberF, MemberG);
            setup.Side2 = ImmutableHashSet.Create(MemberC, MemberD, MemberE);

            var strategy1 = setup.Strategy(setup.Side1);
            var decision1 = strategy1.Decide();
            decision1.Should().BeOfType<DownUnreachable>();
            strategy1.NodesToDown(decision1).Should().BeEquivalentTo(setup.Side2Nodes);

            // F and G were moved to Up by side1 at the same time as the partition, and that has not been seen by
            // side2 so they are still joining
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, Joining(MemberF), Joining(MemberG));
            var strategy2 = setup.Strategy(setup.Side2);
            var decision2 = strategy2.Decide();
            decision2.Should().BeOfType<DownAll>();
            strategy2.NodesToDown(decision2).Should().BeEquivalentTo(setup.Side1Nodes.Union(setup.Side2Nodes));
        }

        [Fact]
        public void KeepMajority_must_detect_edge_case_of_membership_change_when_equal_size_A_B_F__C_D_E___A_B_F()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberF);
            setup.Side2 = ImmutableHashSet.Create(MemberC, MemberD, MemberE);

            var strategy1 = setup.Strategy(setup.Side1);
            var decision1 = strategy1.Decide();
            // memberA is lowest address
            decision1.Should().BeOfType<DownUnreachable>();
            strategy1.NodesToDown(decision1).Should().BeEquivalentTo(setup.Side2Nodes);

            // F was moved to Up by side1 at the same time as the partition, and that has not been seen by
            // side2 so it is still joining
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, Joining(MemberF));
            var strategy2 = setup.Strategy(setup.Side2);
            var decision2 = strategy2.Decide();
            // when counting the joining F it becomes equal size
            decision2.Should().BeOfType<DownAll>();
            strategy2.NodesToDown(decision2).Should().BeEquivalentTo(setup.Side1Nodes.Union(setup.Side2Nodes));
        }

        [Fact]
        public void KeepMajority_must_detect_safe_edge_case_of_membership_change_A_B__C_D_E_F___C_D_E_F()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB);
            setup.Side2 = ImmutableHashSet.Create(MemberC, MemberD, MemberE, Joining(MemberF));

            var strategy1 = setup.Strategy(setup.Side1);
            var decision1 = strategy1.Decide();
            decision1.Should().BeOfType<DownReachable>();
            strategy1.NodesToDown(decision1).Should().BeEquivalentTo(setup.Side1Nodes);

            // F was moved to Up by side2 at the same time as the partition
            setup.Side2 = ImmutableHashSet.Create(MemberC, MemberD, MemberE, MemberF);
            var strategy2 = setup.Strategy(setup.Side2);
            var decision2 = strategy2.Decide();
            decision2.Should().BeOfType<DownUnreachable>();
            strategy2.NodesToDown(decision2).Should().BeEquivalentTo(setup.Side1Nodes);
        }

        [Fact]
        public void KeepMajority_must_detect_edge_case_of_leaving_exiting_membership_change_A_B__C_D___C_D()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(Leaving(MemberA), MemberB, Joining(MemberE));
            setup.Side2 = ImmutableHashSet.Create(MemberC, MemberD);

            var strategy1 = setup.Strategy(setup.Side1);
            var decision1 = strategy1.Decide();
            decision1.Should().BeOfType<DownAll>();
            strategy1.NodesToDown(decision1).Should().BeEquivalentTo(setup.Side1Nodes.Union(setup.Side2Nodes));

            // A was moved to Exiting by side2 at the same time as the partition, and that has not been seen by
            // side1 so it is still Leaving there
            setup.Side1 = ImmutableHashSet.Create(Exiting(MemberA), MemberB);
            var strategy2 = setup.Strategy(setup.Side2);
            var decision2 = strategy2.Decide();
            decision2.Should().BeOfType<DownUnreachable>();
            // A is already Exiting so not downed
            strategy2.NodesToDown(decision2).Should().BeEquivalentTo(setup.Side1Nodes.Remove(MemberA.UniqueAddress));
        }

        [Fact]
        public void KeepMajority_must_down_indirectly_connected_A_B___()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB);
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberA, MemberB),
                (MemberB, MemberA));
            setup.AssertDowning(new[] { MemberA, MemberB });
        }

        [Fact]
        public void KeepMajority_must_down_indirectly_connected_A_B__C___C()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberC);
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberA, MemberB),
                (MemberB, MemberA));
            // keep fully connected memberC
            setup.AssertDowning(new[] { MemberA, MemberB });
        }

        [Fact]
        public void KeepMajority_must_down_indirectly_connected_A_B__C___()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberC);
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberA, MemberB),
                (MemberB, MemberC),
                (MemberC, MemberA));
            setup.AssertDowning(new[] { MemberA, MemberB, MemberC });
        }

        [Fact]
        public void KeepMajority_must_down_indirectly_connected_A_B_C_D___()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberC, MemberD);
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberA, MemberD),
                (MemberD, MemberA),
                (MemberB, MemberC),
                (MemberC, MemberB));
            setup.AssertDowning(new[] { MemberA, MemberB, MemberC, MemberD });
        }

        [Fact]
        public void KeepMajority_must_down_indirectly_connected_A_B_C__D_E___D_E()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberC, MemberD, MemberE);
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberA, MemberB),
                (MemberB, MemberC),
                (MemberC, MemberA));
            // keep fully connected memberD, memberE
            setup.AssertDowning(new[] { MemberA, MemberB, MemberC });
        }

        [Fact]
        public void KeepMajority_must_down_indirectly_connected_A__B_C__D__E_F__G___A_D_G()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberC, MemberD, MemberE, MemberF, MemberG);
            // two groups of indirectly connected, 4 in total
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberB, MemberC),
                (MemberC, MemberB),
                (MemberE, MemberF),
                (MemberF, MemberE));
            // keep fully connected memberA, memberD, memberG
            setup.AssertDowning(new[] { MemberB, MemberC, MemberE, MemberF });
        }

        [Fact]
        public void KeepMajority_must_down_indirectly_connected_detected_via_seen_A_B_C___()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberC);
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberA, MemberB),
                (MemberA, MemberC));
            setup.AssertDowning(new[] { MemberA, MemberB, MemberC });
        }

        [Fact]
        public void KeepMajority_must_down_indirectly_connected_detected_via_seen_A_B_C_D__E___E()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberC, MemberD, MemberE);
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberB, MemberC),
                (MemberC, MemberB),
                (MemberA, MemberD));
            // keep fully connected memberE
            setup.AssertDowning(new[] { MemberA, MemberB, MemberC, MemberD });
        }

        [Fact]
        public void KeepMajority_must_down_indirectly_connected_when_combined_with_crashed_A_B__D_E__C___D_E()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberD, MemberE);
            setup.Side2 = ImmutableHashSet.Create(MemberC);
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberA, MemberB),
                (MemberB, MemberA));
            // keep fully connected memberD, memberE
            // note that crashed memberC is also downed
            setup.AssertDowningSide(setup.Side1, new[] { MemberA, MemberB, MemberC });
        }

        [Fact]
        public void KeepMajority_must_down_indirectly_connected_when_combined_with_clean_partition_A__B_C__D_E___A()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberC);
            setup.Side2 = ImmutableHashSet.Create(MemberD, MemberE);
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberB, MemberC),
                (MemberC, MemberB));
            // from side1 of the partition
            // keep fully connected memberA
            // note that memberD and memberE on the other side of the partition are also downed because side1
            // is majority of clean partition
            setup.AssertDowningSide(setup.Side1, new[] { MemberB, MemberC, MemberD, MemberE });

            // from side2 of the partition
            // indirectly connected not seen from this side, if clean partition happened first
            setup.IndirectlyConnected = ImmutableHashSet<(Member, Member)>.Empty;
            // Note that memberC is not downed, as on the other side, because those indirectly connected
            // not seen from this side. That outcome is OK.
            setup.AssertDowningSide(setup.Side2, new[] { MemberD, MemberE });

            // alternative scenario from side2 of the partition
            // indirectly connected on side1 happens before the clean partition
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberB, MemberC),
                (MemberC, MemberB));
            setup.AssertDowningSide(setup.Side2, new[] { MemberB, MemberC, MemberD, MemberE });
        }

        [Fact]
        public void KeepMajority_must_down_indirectly_connected_on_minority_side_when_combined_with_clean_partition_A__B_C__D_E_F_G___D_E_F_G()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberC);
            setup.Side2 = ImmutableHashSet.Create(MemberD, MemberE, MemberF, MemberG);
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberB, MemberC),
                (MemberC, MemberB));
            // from side1 of the partition, minority
            setup.AssertDowningSide(setup.Side1, new[] { MemberA, MemberB, MemberC });

            // from side2 of the partition, majority
            // indirectly connected not seen from this side, if clean partition happened first
            setup.IndirectlyConnected = ImmutableHashSet<(Member, Member)>.Empty;
            setup.AssertDowningSide(setup.Side2, new[] { MemberA, MemberB, MemberC });

            // alternative scenario from side2 of the partition
            // indirectly connected on side1 happens before the clean partition
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberB, MemberC),
                (MemberC, MemberB));
            setup.AssertDowningSide(setup.Side2, new[] { MemberA, MemberB, MemberC });
        }

        [Fact]
        public void KeepMajority_must_down_indirectly_connected_on_majority_side_when_combined_with_clean_partition_A_B_C__D_E__F_G___F_G()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberC);
            setup.Side2 = ImmutableHashSet.Create(MemberD, MemberE, MemberF, MemberG);

            // from side1 of the partition, minority
            // indirectly connected not seen from this side, if clean partition happened first
            setup.IndirectlyConnected = ImmutableHashSet<(Member, Member)>.Empty;
            setup.AssertDowningSide(setup.Side1, new[] { MemberA, MemberB, MemberC });

            // alternative scenario from side1 of the partition
            // indirectly connected on side2 happens before the clean partition
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberD, MemberE),
                (MemberE, MemberD));
            // note that indirectly connected memberD and memberE are also downed
            setup.AssertDowningSide(setup.Side1, new[] { MemberA, MemberB, MemberC, MemberD, MemberE });

            // from side2 of the partition, majority
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberD, MemberE),
                (MemberE, MemberD));
            setup.AssertDowningSide(setup.Side2, new[] { MemberA, MemberB, MemberC, MemberD, MemberE });
        }

        [Fact]
        public void KeepMajority_must_down_indirectly_connected_spanning_across_a_clean_partition_A__B__C__D__E_F__G___D_G()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberC);
            setup.Side2 = ImmutableHashSet.Create(MemberD, MemberE, MemberF, MemberG);
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberB, MemberE),
                (MemberE, MemberF),
                (MemberF, MemberB));

            // from side1 of the partition, minority
            setup.AssertDowningSide(setup.Side1, new[] { MemberA, MemberB, MemberC, MemberE, MemberF });

            // from side2 of the partition,  majority
            setup.AssertDowningSide(setup.Side2, new[] { MemberA, MemberB, MemberC, MemberE, MemberF });
        }

        [Fact]
        public void KeepMajority_must_down_indirectly_connected_detected_via_seen_combined_with_clean_partition_A_B_C__D_E__F_G___()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberC);
            setup.Side2 = ImmutableHashSet.Create(MemberD, MemberE, MemberF, MemberG);

            // from side1 of the partition, minority
            setup.AssertDowningSide(setup.Side1, new[] { MemberA, MemberB, MemberC });

            // from side2 of the partition,  majority
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberD, MemberE),
                (MemberG, MemberF));
            setup.AssertDowningSide(setup.Side2, new[] { MemberA, MemberB, MemberC, MemberD, MemberE, MemberF, MemberG });
        }

        [Fact]
        public void KeepMajority_must_double_DownIndirectlyConnected_when_indirectly_connected_happens_before_clean_partition_A_B_C__D_E__F_G___()
        {
            var setup = new KeepMajoritySetup(this, null);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberC);
            setup.Side2 = ImmutableHashSet.Create(MemberD, MemberE, MemberF, MemberG);
            // trouble when indirectly connected happens before clean partition
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberD, MemberE),
                (MemberG, MemberF));

            // from side1 of the partition, minority
            // D and G are observers and marked E and F as unreachable
            // A has marked D and G as unreachable
            // The records D->E, G->F are not removed in the second decision because they are not detected via seenB
            // due to clean partition. That means that the second decision will also be DownIndirectlyConnected. To bail
            // out from this situation the strategy will throw IllegalStateException, which is caught and translated to
            // DownAll.
            Assert.ThrowsAny<InvalidOperationException>(() =>
            {
                setup.AssertDowningSide(setup.Side1, new[] { MemberA, MemberB, MemberC });
            });

            // from side2 of the partition, majority
            setup.AssertDowningSide(setup.Side2, new[] { MemberA, MemberB, MemberC, MemberD, MemberE, MemberF, MemberG });
        }


        internal class KeepOldestSetup : StrategySetup
        {
            public KeepOldestSetup(SplitBrainResolverSpec owner, bool downIfAlone = true, string role = null)
                : base(owner)
            {
                DownIfAlone = downIfAlone;
                Role = role;
            }

            public bool DownIfAlone { get; }
            public string Role { get; }

            public override DowningStrategy CreateStrategy()
            {
                return new Akka.Cluster.SBR.KeepOldest(DownIfAlone, Role);
            }
        }

        [Fact]
        public void KeepOldest_must_keep_partition_with_oldest()
        {
            var setup = new KeepOldestSetup(this);
            // E is the oldest
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberE);
            setup.Side2 = ImmutableHashSet.Create(MemberB, MemberC, MemberD);
            setup.AssertDowning(setup.Side2);
        }

        [Fact]
        public void KeepOldest_must_keep_partition_with_oldest_with_role()
        {
            var setup = new KeepOldestSetup(this, role: "role2");
            // C and D have role2, D is the oldest
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberE);
            setup.Side2 = ImmutableHashSet.Create(MemberB, MemberC, MemberD);
            setup.AssertDowning(setup.Side1);
        }

        [Fact]
        public void KeepOldest_must_keep_partition_with_oldest_unless_alone()
        {
            var setup = new KeepOldestSetup(this, downIfAlone: true);
            setup.Side1 = ImmutableHashSet.Create(MemberE);
            setup.Side2 = ImmutableHashSet.Create(MemberA, MemberB, MemberC, MemberD);
            setup.AssertDowning(setup.Side1);
        }

        [Fact]
        public void KeepOldest_must_keep_partition_with_oldest_in_two_nodes_cluster()
        {
            var setup = new KeepOldestSetup(this);
            setup.Side1 = ImmutableHashSet.Create(MemberB);
            setup.Side2 = ImmutableHashSet.Create(MemberA);
            setup.AssertDowning(setup.Side2);
        }

        [Fact]
        public void KeepOldest_must_keep_one_single_oldest()
        {
            var setup = new KeepOldestSetup(this);
            setup.Side1 = ImmutableHashSet<Member>.Empty;
            setup.Side2 = ImmutableHashSet.Create(MemberA);
            setup.AssertDowning(setup.Side1);
        }

        [Fact]
        public void KeepOldest_must_keep_oldest_even_when_alone_when_downIfAlone_eq_false()
        {
            var setup = new KeepOldestSetup(this, downIfAlone: false);
            setup.Side1 = ImmutableHashSet.Create(MemberE);
            setup.Side2 = ImmutableHashSet.Create(MemberA, MemberB, MemberC, MemberD);
            setup.AssertDowning(setup.Side2);
        }

        [Fact]
        public void KeepOldest_must_detect_leaving_exiting_edge_case_keep_partition_with_oldest_scenario_1()
        {
            var setup = new KeepOldestSetup(this);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberD);
            setup.Side2 = ImmutableHashSet.Create(MemberC, Exiting(MemberE));

            var strategy1 = setup.Strategy(setup.Side1);
            var decision1 = strategy1.Decide();
            // E is Exiting so not counted as oldest, D is oldest
            decision1.Should().BeOfType<DownUnreachable>();
            // side2 is downed, but E is already exiting and therefore not downed
            strategy1.NodesToDown(decision1).Should().BeEquivalentTo(setup.Side2Nodes.Remove(MemberE.UniqueAddress));

            // E was changed to Exiting by side1 but that is not seen on side2 due to the partition, so still Leaving
            setup.Side2 = ImmutableHashSet.Create(MemberC, Leaving(MemberE));
            var strategy2 = setup.Strategy(setup.Side2);
            var decision2 = strategy2.Decide();

            decision2.Should().BeOfType<DownAll>();
            strategy2.NodesToDown(decision2).Should().BeEquivalentTo(setup.Side1Nodes.Union(setup.Side2Nodes));
        }

        [Fact]
        public void KeepOldest_must_detect_leaving_exiting_edge_case_keep_partition_with_oldest_scenario_2()
        {
            var setup = new KeepOldestSetup(this);
            setup.Side1 = ImmutableHashSet.Create(MemberA, Leaving(MemberE));
            setup.Side2 = ImmutableHashSet.Create(MemberB, MemberC, MemberD);

            setup.Strategy(setup.Side1).Decide().Should().BeOfType<DownAll>();
            setup.Strategy(setup.Side2).Decide().Should().BeOfType<DownReachable>();
        }

        [Fact]
        public void KeepOldest_must_detect_leaving_exiting_edge_case_keep_partition_with_oldest_scenario_3()
        {
            var setup = new KeepOldestSetup(this);
            // E is the oldest
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberE);
            setup.Side2 = ImmutableHashSet.Create(Leaving(MemberB), Leaving(MemberC), MemberD);
            setup.AssertDowning(setup.Side2);
        }

        [Fact]
        public void KeepOldest_must_detect_leaving_exiting_edge_case_keep_partition_with_oldest_unless_alone_scenario_1()
        {
            var setup = new KeepOldestSetup(this);
            setup.Side1 = ImmutableHashSet.Create(Leaving(MemberD), MemberE);
            setup.Side2 = ImmutableHashSet.Create(MemberA, MemberB, MemberC);
            setup.Strategy(setup.Side1).Decide().Should().BeOfType<DownAll>();
            setup.Strategy(setup.Side2).Decide().Should().BeOfType<DownReachable>();
        }

        [Fact]
        public void KeepOldest_must_detect_leaving_exiting_edge_case_keep_partition_with_oldest_unless_alone_scenario_4()
        {
            var setup = new KeepOldestSetup(this);
            setup.Side1 = ImmutableHashSet.Create(MemberE);
            setup.Side2 = ImmutableHashSet.Create(MemberA, MemberB, Leaving(MemberC));
            setup.Strategy(setup.Side1).Decide().Should().BeOfType<DownReachable>();
            setup.Strategy(setup.Side2).Decide().Should().BeOfType<DownUnreachable>();
        }

        [Fact]
        public void KeepOldest_must_detect_leaving_exiting_edge_case_keep_partition_with_oldest_unless_alone_scenario_3()
        {
            var setup = new KeepOldestSetup(this);
            setup.Side1 = ImmutableHashSet.Create(MemberE);
            setup.Side2 = ImmutableHashSet.Create(MemberA, Leaving(MemberB), Leaving(MemberC), Leaving(MemberD));
            setup.Strategy(setup.Side1).Decide().Should().BeOfType<DownReachable>();
            setup.Strategy(setup.Side2).Decide().Should().BeOfType<DownAll>();
        }

        [Fact]
        public void KeepOldest_must_detect_leaving_exiting_edge_case_DownReachable_on_both_sides_when_oldest_leaving_exiting_is_alone()
        {
            var setup = new KeepOldestSetup(this);
            setup.Side1 = ImmutableHashSet.Create(MemberD, Exiting(MemberE));
            setup.Side2 = ImmutableHashSet.Create(MemberA, MemberB, MemberC);
            // E is Exiting so not counted as oldest, D is oldest, but it's alone so keep side2 anyway
            setup.Strategy(setup.Side1).Decide().Should().BeOfType<DownReachable>();

            // E was changed to Exiting by side1 but that is not seen on side2 due to the partition, so still Leaving
            setup.Side1 = ImmutableHashSet.Create(MemberD, Leaving(MemberE));
            setup.Strategy(setup.Side2).Decide().Should().BeOfType<DownReachable>();
        }

        [Fact]
        public void KeepOldest_must_detect_leaving_exiting_edge_case_when_one_single_oldest()
        {
            var setup = new KeepOldestSetup(this);
            setup.Side1 = ImmutableHashSet.Create(MemberA);
            setup.Side2 = ImmutableHashSet.Create(Exiting(MemberB));
            // B is Exiting so not counted as oldest, A is oldest
            setup.Strategy(setup.Side1).Decide().Should().BeOfType<DownUnreachable>();

            // B was changed to Exiting by side1 but that is not seen on side2 due to the partition, so still Leaving
            setup.Side2 = ImmutableHashSet.Create(Leaving(MemberB));
            setup.Strategy(setup.Side2).Decide().Should().BeOfType<DownAll>();
        }

        [Fact]
        public void KeepOldest_must_detect_joining_up_edge_keep_partition_with_oldest_unless_alone_scenario_1()
        {
            var setup = new KeepOldestSetup(this);
            setup.Side1 = ImmutableHashSet.Create(Joining(MemberA), MemberE);
            setup.Side2 = ImmutableHashSet.Create(MemberB, MemberC, MemberD);
            // E alone when not counting joining A
            setup.Strategy(setup.Side1).Decide().Should().BeOfType<DownReachable>();
            // but A could have been up on other side1 and therefore side2 has to down all
            setup.Strategy(setup.Side2).Decide().Should().BeOfType<DownAll>();
        }

        [Fact]
        public void KeepOldest_must_detect_joining_up_edge_keep_oldest_even_when_alone_when_downIfAlone_eq_false()
        {
            var setup = new KeepOldestSetup(this, downIfAlone: false);
            setup.Side1 = ImmutableHashSet.Create(Joining(MemberA), MemberE);
            setup.Side2 = ImmutableHashSet.Create(MemberB, MemberC, MemberD);
            // joining A shouldn't matter when downIfAlone = false
            setup.Strategy(setup.Side1).Decide().Should().BeOfType<DownUnreachable>();
            setup.Strategy(setup.Side2).Decide().Should().BeOfType<DownReachable>();
        }

        [Fact]
        public void KeepOldest_must_down_indirectly_connected_A_B__C___C()
        {
            var setup = new KeepOldestSetup(this);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberC);
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberA, MemberB),
                (MemberB, MemberA));
            setup.AssertDowning(new[] { MemberA, MemberB });
        }

        [Fact]
        public void KeepOldest_must_down_indirectly_connected_on_younger_side_when_combined_with_clean_partition_A__B_C__D_E_F_G___D_E_F_G()
        {
            var setup = new KeepOldestSetup(this);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberC);
            setup.Side2 = ImmutableHashSet.Create(MemberD, MemberE, MemberF, MemberG);
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberB, MemberC),
                (MemberC, MemberB));

            // from side1 of the partition, younger
            setup.AssertDowningSide(setup.Side1, new[] { MemberA, MemberB, MemberC });

            // from side2 of the partition, oldest
            // indirectly connected not seen from this side, if clean partition happened first
            setup.IndirectlyConnected = ImmutableHashSet<(Member, Member)>.Empty;
            setup.AssertDowningSide(setup.Side2, new[] { MemberA, MemberB, MemberC });

            // alternative scenario from side2 of the partition
            // indirectly connected on side1 happens before the clean partition
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberB, MemberC),
                (MemberC, MemberB));
            setup.AssertDowningSide(setup.Side2, new[] { MemberA, MemberB, MemberC });
        }

        [Fact]
        public void KeepOldest_must_down_indirectly_connected_on_oldest_side_when_combined_with_clean_partition_A_B_C__D_E__F_G___F_G()
        {
            var setup = new KeepOldestSetup(this);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberC);
            setup.Side2 = ImmutableHashSet.Create(MemberD, MemberE, MemberF, MemberG);

            // from side1 of the partition, younger
            // indirectly connected not seen from this side, if clean partition happened first
            setup.IndirectlyConnected = ImmutableHashSet<(Member, Member)>.Empty;
            setup.AssertDowningSide(setup.Side1, new[] { MemberA, MemberB, MemberC });

            // alternative scenario from side1 of the partition
            // indirectly connected on side2 happens before the clean partition
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberD, MemberE),
                (MemberE, MemberD));
            // note that indirectly connected memberD and memberE are also downed
            setup.AssertDowningSide(setup.Side1, new[] { MemberA, MemberB, MemberC, MemberD, MemberE });

            // from side2 of the partition, oldest
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberD, MemberE),
                (MemberE, MemberD));
            setup.AssertDowningSide(setup.Side2, new[] { MemberA, MemberB, MemberC, MemberD, MemberE });
        }


        internal class DownAllNodesSetup : StrategySetup
        {
            public DownAllNodesSetup(SplitBrainResolverSpec owner)
                : base(owner)
            {
            }

            public override DowningStrategy CreateStrategy()
            {
                return new Akka.Cluster.SBR.DownAllNodes();
            }
        }

        [Fact]
        public void DownAllNodes_must_down_all()
        {
            var setup = new DownAllNodesSetup(this);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberC);
            setup.Side2 = ImmutableHashSet.Create(MemberD, MemberE);
            setup.AssertDowning(setup.Side1.Union(setup.Side2));
        }


        internal class LeaseMajoritySetup : StrategySetup
        {
            public LeaseMajoritySetup(SplitBrainResolverSpec owner, string role = null)
                : base(owner)
            {
                Role = role;

                TestLease = new TestLease(owner.testLeaseSettings, owner.ExtSystem);

                AcquireLeaseDelayForMinority = TimeSpan.FromSeconds(2);
            }

            public string Role { get; }
            public TestLease TestLease { get; }
            public TimeSpan AcquireLeaseDelayForMinority { get; }

            public override DowningStrategy CreateStrategy()
            {
                return new LeaseMajority(Role, TestLease, AcquireLeaseDelayForMinority);
            }
        }

        [Fact]
        public void LeaseMajority_must_decide_AcquireLeaseAndDownUnreachable_and_DownReachable_as_reverse_decision()
        {
            var setup = new LeaseMajoritySetup(this);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberC, MemberE);
            setup.Side2 = ImmutableHashSet.Create(MemberB, MemberD);

            var strategy1 = setup.Strategy(setup.Side1);
            var decision1 = strategy1.Decide();
            decision1.Should().Be(new AcquireLeaseAndDownUnreachable(TimeSpan.Zero));
            strategy1.NodesToDown(decision1).Should().BeEquivalentTo(setup.Side2Nodes);
            var reverseDecision1 = strategy1.ReverseDecision(decision1);
            reverseDecision1.Should().BeOfType<DownReachable>();
            strategy1.NodesToDown(reverseDecision1).Should().BeEquivalentTo(setup.Side1Nodes);

            var strategy2 = setup.Strategy(setup.Side2);
            var decision2 = strategy2.Decide();
            decision2.Should().Be(new AcquireLeaseAndDownUnreachable(setup.AcquireLeaseDelayForMinority));
            strategy2.NodesToDown(decision2).Should().BeEquivalentTo(setup.Side1Nodes);
            var reverseDecision2 = strategy2.ReverseDecision(decision2);
            reverseDecision2.Should().BeOfType<DownReachable>();
            strategy2.NodesToDown(reverseDecision2).Should().BeEquivalentTo(setup.Side2Nodes);
        }

        [Fact]
        public void LeaseMajority_must_try_to_keep_half_with_lowest_address_when_equal_size_partition()
        {
            var setup = new LeaseMajoritySetup(this, "role2");
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberD, MemberE);
            setup.Side2 = ImmutableHashSet.Create(MemberB, MemberC);
            // memberC is lowest with role2
            var strategy1 = setup.Strategy(setup.Side1);
            var decision1 = strategy1.Decide();
            // delay on side1 because memberC is lowest address with role2
            decision1.Should().Be(new AcquireLeaseAndDownUnreachable(setup.AcquireLeaseDelayForMinority));
            strategy1.NodesToDown(decision1).Should().BeEquivalentTo(setup.Side2Nodes);

            var strategy2 = setup.Strategy(setup.Side2);
            var decision2 = strategy2.Decide();
            decision2.Should().Be(new AcquireLeaseAndDownUnreachable(TimeSpan.Zero));
            strategy2.NodesToDown(decision2).Should().BeEquivalentTo(setup.Side1Nodes);
        }

        [Fact]
        public void LeaseMajority_must_down_indirectly_connected_A_B__C___C()
        {
            var setup = new LeaseMajoritySetup(this);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberC);
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberA, MemberB),
                (MemberB, MemberA));

            var strategy1 = setup.Strategy(setup.Side1);
            var decision1 = strategy1.Decide();
            decision1.Should().Be(new AcquireLeaseAndDownIndirectlyConnected(TimeSpan.Zero));
            strategy1.NodesToDown(decision1).Should().BeEquivalentTo(new[] { MemberA.UniqueAddress, MemberB.UniqueAddress });
            var reverseDecision1 = strategy1.ReverseDecision(decision1);
            reverseDecision1.Should().BeOfType<ReverseDownIndirectlyConnected>();
            strategy1.NodesToDown(reverseDecision1).Should().BeEquivalentTo(setup.Side1Nodes);
        }

        [Fact]
        public void LeaseMajority_must_down_indirectly_connected_when_combined_with_clean_partition_A__B_C__D_E___A()
        {
            var setup = new LeaseMajoritySetup(this);
            setup.Side1 = ImmutableHashSet.Create(MemberA, MemberB, MemberC);
            setup.Side2 = ImmutableHashSet.Create(MemberD, MemberE);
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberB, MemberC),
                (MemberC, MemberB));

            // from side1 of the partition
            // keep fully connected memberA
            // note that memberD and memberE on the other side of the partition are also downed
            var strategy1 = setup.Strategy(setup.Side1);
            var decision1 = strategy1.Decide();
            decision1.Should().Be(new AcquireLeaseAndDownIndirectlyConnected(TimeSpan.Zero));
            strategy1.NodesToDown(decision1).Should().BeEquivalentTo(new[] { MemberB, MemberC, MemberD, MemberE }.Select(m => m.UniqueAddress));
            var reverseDecision1 = strategy1.ReverseDecision(decision1);
            reverseDecision1.Should().BeOfType<ReverseDownIndirectlyConnected>();
            strategy1.NodesToDown(reverseDecision1).Should().BeEquivalentTo(setup.Side1Nodes);

            // from side2 of the partition
            // indirectly connected not seen from this side, if clean partition happened first
            setup.IndirectlyConnected = ImmutableHashSet<(Member, Member)>.Empty;
            // Note that memberC is not downed, as on the other side, because those indirectly connected
            // not seen from this side. That outcome is OK.
            var strategy2 = setup.Strategy(setup.Side2);
            var decision2 = strategy2.Decide();
            decision2.Should().Be(new AcquireLeaseAndDownUnreachable(setup.AcquireLeaseDelayForMinority));
            strategy2.NodesToDown(decision2).Should().BeEquivalentTo(setup.Side1Nodes);
            var reverseDecision2 = strategy2.ReverseDecision(decision2);
            reverseDecision2.Should().BeOfType<DownReachable>();
            strategy2.NodesToDown(reverseDecision2).Should().BeEquivalentTo(setup.Side2Nodes);

            // alternative scenario from side2 of the partition
            // indirectly connected on side1 happens before the clean partition
            setup.IndirectlyConnected = ImmutableHashSet.Create(
                (MemberB, MemberC),
                (MemberC, MemberB));
            var strategy3 = setup.Strategy(setup.Side2);
            var decision3 = strategy3.Decide();
            decision3.Should().Be(new AcquireLeaseAndDownIndirectlyConnected(TimeSpan.Zero));
            strategy3.NodesToDown(decision3).Should().BeEquivalentTo(setup.Side1Nodes);
            var reverseDecision3 = strategy3.ReverseDecision(decision3);
            reverseDecision3.Should().BeOfType<ReverseDownIndirectlyConnected>();
            strategy3.NodesToDown(reverseDecision3).Should().BeEquivalentTo(new[] { MemberB, MemberC, MemberD, MemberE }.Select(m => m.UniqueAddress));
        }

        [Fact]
        public void Strategy_must_add_and_remove_members_with_default_Member_ordering()
        {
            var setup = new KeepMajoritySetup(this);
            setup.Side1 = ImmutableHashSet<Member>.Empty;
            setup.Side2 = ImmutableHashSet<Member>.Empty;

            var strategy1 = setup.Strategy(setup.Side1);
            TestAddRemove(strategy1);
        }

        [Fact]
        public void Strategy_must_add_and_remove_members_with_oldest_Member_ordering()
        {
            var setup = new KeepOldestSetup(this);
            setup.Side1 = ImmutableHashSet<Member>.Empty;
            setup.Side2 = ImmutableHashSet<Member>.Empty;

            TestAddRemove(setup.Strategy(setup.Side1));
        }

        private void TestAddRemove(DowningStrategy strategy)
        {
            strategy.Add(Joining(MemberA));
            strategy.Add(Joining(MemberB));
            strategy.AllMembers.Count.Should().Be(2);
            strategy.AllMembers.All(m => m.Status == MemberStatus.Joining).Should().BeTrue();
            strategy.Add(MemberA);
            strategy.Add(MemberB);
            strategy.AllMembers.Count.Should().Be(2);
            strategy.AllMembers.All(m => m.Status == MemberStatus.Up).Should().BeTrue();
            strategy.Add(Leaving(MemberB));
            strategy.AllMembers.Count.Should().Be(2);
            strategy.AllMembers.Select(i => i.Status).ToImmutableHashSet().Should().BeEquivalentTo(new[] { MemberStatus.Up, MemberStatus.Leaving });
            strategy.Add(Exiting(MemberB));
            strategy.AllMembers.Count.Should().Be(2);
            strategy.AllMembers.Select(i => i.Status).ToImmutableHashSet().Should().BeEquivalentTo(new[] { MemberStatus.Up, MemberStatus.Exiting });
            strategy.Remove(MemberA);
            strategy.AllMembers.Count.Should().Be(1);
            strategy.AllMembers.FirstOrDefault().Status.Should().Be(MemberStatus.Exiting);
        }

        [Fact]
        public void Strategy_must_collect_and_filter_members_with_default_Member_ordering()
        {
            var setup = new KeepMajoritySetup(this);
            setup.Side1 = ImmutableHashSet<Member>.Empty;
            setup.Side2 = ImmutableHashSet<Member>.Empty;

            TestCollectAndFilter(setup);
        }

        [Fact]
        public void Strategy_must_collect_and_filter_members_with_oldest_Member_ordering()
        {
            var setup = new KeepOldestSetup(this);
            setup.Side1 = ImmutableHashSet<Member>.Empty;
            setup.Side2 = ImmutableHashSet<Member>.Empty;

            TestCollectAndFilter(setup);
        }

        private void TestCollectAndFilter(StrategySetup setup)
        {
            setup.Side1 = ImmutableHashSet.Create(MemberAWeaklyUp, MemberB, Joining(MemberC));
            setup.Side2 = ImmutableHashSet.Create(MemberD, Leaving(MemberE), Downed(MemberF), Exiting(MemberG));

            var strategy1 = setup.Strategy(setup.Side1);

            strategy1.MembersWithRole.Should().BeEquivalentTo(new[] { MemberB, MemberD, Leaving(MemberE) });
            strategy1.GetMembersWithRole(includingPossiblyUp: true, excludingPossiblyExiting: false).Should().BeEquivalentTo(
                new[] { MemberAWeaklyUp, MemberB, Joining(MemberC), MemberD, Leaving(MemberE) });
            strategy1.GetMembersWithRole(includingPossiblyUp: false, excludingPossiblyExiting: true).Should().BeEquivalentTo(
                new[] { MemberB, MemberD });
            strategy1.GetMembersWithRole(includingPossiblyUp: true, excludingPossiblyExiting: true).Should().BeEquivalentTo(
                new[] { MemberAWeaklyUp, MemberB, Joining(MemberC), MemberD });

            strategy1.ReachableMembersWithRole.Should().BeEquivalentTo(new[] { MemberB });
            strategy1.GetReachableMembersWithRole(includingPossiblyUp: true, excludingPossiblyExiting: false).Should().BeEquivalentTo(
                new[] { MemberAWeaklyUp, MemberB, Joining(MemberC) });
            strategy1.GetReachableMembersWithRole(includingPossiblyUp: false, excludingPossiblyExiting: true).Should().BeEquivalentTo(
                new[] { MemberB });
            strategy1.GetReachableMembersWithRole(includingPossiblyUp: true, excludingPossiblyExiting: true).Should().BeEquivalentTo(
                new[] { MemberAWeaklyUp, MemberB, Joining(MemberC) });

            strategy1.UnreachableMembersWithRole.Should().BeEquivalentTo(new[] { MemberD, Leaving(MemberE) });
            strategy1.GetUnreachableMembers(includingPossiblyUp: true, excludingPossiblyExiting: false).Should().BeEquivalentTo(
                new[] { MemberD, Leaving(MemberE) });
            strategy1.GetUnreachableMembers(includingPossiblyUp: false, excludingPossiblyExiting: true).Should().BeEquivalentTo(new[] { MemberD });
            strategy1.GetUnreachableMembers(includingPossiblyUp: true, excludingPossiblyExiting: true).Should().BeEquivalentTo(new[] { MemberD });

            strategy1.IsUnreachable(MemberAWeaklyUp).Should().BeFalse();
            strategy1.IsUnreachable(MemberB).Should().BeFalse();
            strategy1.IsUnreachable(MemberD).Should().BeTrue();
            strategy1.IsUnreachable(Leaving(MemberE)).Should().BeTrue();
            strategy1.IsUnreachable(Downed(MemberF)).Should().BeTrue();
            strategy1.Joining.Should().BeEquivalentTo(new[] { MemberAWeaklyUp, Joining(MemberC) });

            var strategy2 = setup.Strategy(setup.Side2);

            strategy2.MembersWithRole.Should().BeEquivalentTo(new[] { MemberB, MemberD, Leaving(MemberE) });
            strategy2.GetMembersWithRole(includingPossiblyUp: true, excludingPossiblyExiting: false).Should().BeEquivalentTo(
                new[] { MemberAWeaklyUp, MemberB, Joining(MemberC), MemberD, Leaving(MemberE) });
            strategy2.GetMembersWithRole(includingPossiblyUp: false, excludingPossiblyExiting: true).Should().BeEquivalentTo(
                new[] { MemberB, MemberD });
            strategy2.GetMembersWithRole(includingPossiblyUp: true, excludingPossiblyExiting: true).Should().BeEquivalentTo(
                new[] { MemberAWeaklyUp, MemberB, Joining(MemberC), MemberD });

            strategy2.UnreachableMembersWithRole.Should().BeEquivalentTo(new[] { MemberB });
            strategy2.GetUnreachableMembersWithRole(includingPossiblyUp: true, excludingPossiblyExiting: false).Should().BeEquivalentTo(
                new[] { MemberAWeaklyUp, MemberB, Joining(MemberC) });
            strategy2.GetUnreachableMembersWithRole(includingPossiblyUp: false, excludingPossiblyExiting: true).Should().BeEquivalentTo(
                new[] { MemberB });
            strategy2.GetUnreachableMembersWithRole(includingPossiblyUp: true, excludingPossiblyExiting: true).Should().BeEquivalentTo(
                new[] { MemberAWeaklyUp, MemberB, Joining(MemberC) });

            strategy2.ReachableMembersWithRole.Should().BeEquivalentTo(new[] { MemberD, Leaving(MemberE) });
            strategy2.GetReachableMembers(includingPossiblyUp: true, excludingPossiblyExiting: false).Should().BeEquivalentTo(
                new[] { MemberD, Leaving(MemberE) });
            strategy2.GetReachableMembers(includingPossiblyUp: false, excludingPossiblyExiting: true).Should().BeEquivalentTo(new[] { MemberD });
            strategy2.GetReachableMembers(includingPossiblyUp: true, excludingPossiblyExiting: true).Should().BeEquivalentTo(new[] { MemberD });

            strategy2.IsUnreachable(MemberAWeaklyUp).Should().BeTrue();
            strategy2.IsUnreachable(MemberB).Should().BeTrue();
            strategy2.IsUnreachable(MemberD).Should().BeFalse();
            strategy2.IsUnreachable(Leaving(MemberE)).Should().BeFalse();
            strategy2.IsUnreachable(Downed(MemberF)).Should().BeFalse();
            strategy2.Joining.Should().BeEquivalentTo(new[] { MemberAWeaklyUp, Joining(MemberC) });
        }


        internal class SetupKeepMajority : Setup
        {
            public SetupKeepMajority(
                SplitBrainResolverSpec owner,
                TimeSpan stableAfter,
                UniqueAddress selfUniqueAddress,
                string role,
                TimeSpan? downAllWhenUnstable = null,
                TimeSpan? tickInterval = null)
                : base(
                      owner,
                      stableAfter,
                      new Akka.Cluster.SBR.KeepMajority(role),
                      selfUniqueAddress,
                      downAllWhenUnstable,
                      tickInterval)
            {
            }
        }

        internal class SetupKeepOldest : Setup
        {
            public SetupKeepOldest(
                SplitBrainResolverSpec owner,
                TimeSpan stableAfter,
                UniqueAddress selfUniqueAddress,
                bool downIfAlone,
                string role)
                : base(
                      owner,
                      stableAfter,
                      new Akka.Cluster.SBR.KeepOldest(downIfAlone, role),
                      selfUniqueAddress)
            {
            }
        }

        internal class SetupStaticQuorum : Setup
        {
            public SetupStaticQuorum(
                SplitBrainResolverSpec owner,
                TimeSpan stableAfter,
                UniqueAddress selfUniqueAddress,
                int size,
                string role)
                : base(
                      owner,
                      stableAfter,
                      new Akka.Cluster.SBR.StaticQuorum(size, role),
                      selfUniqueAddress)
            {
            }
        }

        internal class SetupDownAllNodes : Setup
        {
            public SetupDownAllNodes(
                SplitBrainResolverSpec owner,
                TimeSpan stableAfter,
                UniqueAddress selfUniqueAddress)
                : base(
                      owner,
                      stableAfter,
                      new DownAllNodes(),
                      selfUniqueAddress)
            {
            }
        }

        internal class SetupLeaseMajority : Setup
        {
            public SetupLeaseMajority(
                SplitBrainResolverSpec owner,
                TimeSpan stableAfter,
                UniqueAddress selfUniqueAddress,
                string role,
                TestLease testLease,
                TimeSpan? downAllWhenUnstable = null,
                TimeSpan? tickInterval = null)
                : base(
                      owner,
                      stableAfter,
                      new LeaseMajority(role, testLease, acquireLeaseDelayForMinority: TimeSpan.FromMilliseconds(20)),
                      selfUniqueAddress,
                      downAllWhenUnstable,
                      tickInterval)
            {
                TestLease = testLease;
            }

            public TestLease TestLease { get; }
        }

        internal abstract class Setup
        {
            public IActorRef A { get; private set; }

            public Setup(
                SplitBrainResolverSpec owner,
                TimeSpan stableAfter,
                DowningStrategy strategy,
                UniqueAddress selfUniqueAddress,
                TimeSpan? downAllWhenUnstable = null,
                TimeSpan? tickInterval = null)
            {
                Owner = owner;
                StableAfter = stableAfter;
                Strategy = strategy;
                SelfUniqueAddress = selfUniqueAddress;
                DownAllWhenUnstable = downAllWhenUnstable ?? TimeSpan.Zero;
                TickInterval = tickInterval ?? TimeSpan.Zero;

                A = owner.Sys.ActorOf(
                  DowningTestActor
                    .Props(stableAfter, strategy, owner.TestActor, selfUniqueAddress, DownAllWhenUnstable, TickInterval));
            }

            public SplitBrainResolverSpec Owner { get; }
            public TimeSpan StableAfter { get; }
            public DowningStrategy Strategy { get; }
            public UniqueAddress SelfUniqueAddress { get; }
            public TimeSpan DownAllWhenUnstable { get; }
            public TimeSpan TickInterval { get; }

            public void MemberUp(params Member[] members)
            {
                foreach (var m in members)
                    A.Tell(new ClusterEvent.MemberUp(m));
            }

            public void MemberWeaklyUp(params Member[] members)
            {
                foreach (var m in members)
                    A.Tell(new ClusterEvent.MemberWeaklyUp(m));
            }

            public void Leader(Member member)
            {
                A.Tell(new ClusterEvent.LeaderChanged(member.Address));
            }

            public void Unreachable(params Member[] members)
            {
                foreach (var m in members)
                    A.Tell(new ClusterEvent.UnreachableMember(m));
            }

            public void ReachabilityChanged(params (Member, Member)[] unreachability)
            {
                Unreachable(unreachability.Select(i => i.Item2).ToArray());

                var r = Owner.CreateReachability(unreachability);
                A.Tell(new ClusterEvent.ReachabilityChanged(r));
            }

            public void Remove(params Member[] members)
            {
                foreach (var m in members)
                    A.Tell(new ClusterEvent.MemberRemoved(m.Copy(MemberStatus.Removed), MemberStatus.Exiting));
            }

            public void Reachable(params Member[] members)
            {
                foreach (var m in members)
                    A.Tell(new ClusterEvent.ReachableMember(m));
            }

            public void Tick()
            {
                A.Tell(SplitBrainResolverBase.Tick.Instance);
            }

            public void ExpectDownCalled(params Member[] members)
            {
                Owner.ReceiveN(members.Length).ToImmutableHashSet().Should().BeEquivalentTo(members.Select(m => new DownCalled(m.Address)).ToImmutableHashSet());
            }

            public void ExpectNoDecision(TimeSpan max)
            {
                Owner.ExpectNoMsg(max);
            }

            public void Stop()
            {
                Owner.Sys.Stop(A);
                Owner.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            }
        }

        [Fact]
        public void Split_Brain_Resolver_must_have_downRemovalMargin_equal_to_stable_after()
        {
            var cluster = Cluster.Get(Sys);
            var sbrSettings = new SplitBrainResolverSettings(Sys.Settings.Config);
            cluster.DowningProvider.DownRemovalMargin.Should().Be(sbrSettings.DowningStableAfter);
        }

        [Fact]
        public void Split_Brain_Resolver_must_down_unreachable_when_leader()
        {
            var setup = new SetupKeepMajority(this, TimeSpan.Zero, MemberA.UniqueAddress, null);
            setup.MemberUp(MemberA, MemberB, MemberC);
            setup.Leader(MemberA);
            setup.Unreachable(MemberB);
            setup.Tick();
            setup.ExpectDownCalled(MemberB);
            setup.Stop();
        }

        [Fact]
        public void Split_Brain_Resolver_must_not_down_unreachable_when_not_leader()
        {
            var setup = new SetupKeepMajority(this, TimeSpan.Zero, MemberB.UniqueAddress, null);
            setup.MemberUp(MemberA, MemberB, MemberC);
            setup.Leader(MemberA);
            setup.Unreachable(MemberC);
            setup.Tick();
            ExpectNoMsg(500);
            setup.Stop();
        }

        [Fact]
        public void Split_Brain_Resolver_must_down_unreachable_when_becoming_leader()
        {
            var setup = new SetupKeepMajority(this, TimeSpan.Zero, MemberA.UniqueAddress, null);
            setup.MemberUp(MemberA, MemberB, MemberC);
            setup.Leader(MemberB);
            setup.Unreachable(MemberC);
            setup.Leader(MemberA);
            setup.Tick();
            setup.ExpectDownCalled(MemberC);
            setup.Stop();
        }

        [Fact]
        public void Split_Brain_Resolver_must_down_unreachable_after_specified_duration()
        {
            var setup = new SetupKeepMajority(this, TimeSpan.FromSeconds(2), MemberA.UniqueAddress, null);
            setup.MemberUp(MemberA, MemberB, MemberC);
            setup.Leader(MemberA);
            setup.Unreachable(MemberB);
            ExpectNoMsg(1000);
            setup.ExpectDownCalled(MemberB);
            setup.Stop();
        }

        [Fact]
        public void Split_Brain_Resolver_must_down_unreachable_when_becoming_leader_inbetween_detection_and_specified_duration()
        {
            var setup = new SetupKeepMajority(this, TimeSpan.FromSeconds(2), MemberA.UniqueAddress, null);
            setup.MemberUp(MemberA, MemberB, MemberC);
            setup.Leader(MemberB);
            setup.Unreachable(MemberC);
            setup.Leader(MemberA);
            setup.Tick();
            ExpectNoMsg(1000);
            setup.ExpectDownCalled(MemberC);
            setup.Stop();
        }

        [Fact]
        public void Split_Brain_Resolver_must_not_down_unreachable_when_loosing_leadership_inbetween_detection_and_specified_duration()
        {
            var setup = new SetupKeepMajority(this, TimeSpan.FromSeconds(1), MemberA.UniqueAddress, null);
            setup.MemberUp(MemberA, MemberB, MemberC);
            setup.Leader(MemberA);
            setup.Unreachable(MemberC);
            setup.Leader(MemberB);
            setup.Tick();
            ExpectNoMsg(1500);
            setup.Stop();
        }

        [Fact]
        public void Split_Brain_Resolver_must_down_when_becoming_Weakly_Up_leader()
        {
            var setup = new SetupKeepMajority(this, TimeSpan.Zero, MemberAWeaklyUp.UniqueAddress, null);
            setup.MemberUp(MemberC);
            setup.MemberWeaklyUp(MemberAWeaklyUp, MemberBWeaklyUp);
            setup.Unreachable(MemberC);
            setup.Leader(MemberAWeaklyUp);
            setup.Tick();
            setup.ExpectDownCalled(MemberAWeaklyUp, MemberBWeaklyUp);
            setup.Stop();
        }

        [Fact]
        public void Split_Brain_Resolver_must_not_down_when_unreachable_become_reachable_inbetween_detection_and_specified_duration()
        {
            var setup = new SetupKeepMajority(this, TimeSpan.FromSeconds(1), MemberA.UniqueAddress, null);
            setup.MemberUp(MemberA, MemberB, MemberC);
            setup.Leader(MemberA);
            setup.Unreachable(MemberB);
            setup.Reachable(MemberB);
            setup.Tick();
            ExpectNoMsg(1500);
            setup.Stop();
        }

        [Fact]
        public void Split_Brain_Resolver_must_not_down_when_unreachable_is_removed_inbetween_detection_and_specified_duration()
        {
            var setup = new SetupKeepMajority(this, TimeSpan.FromSeconds(1), MemberA.UniqueAddress, null);
            setup.MemberUp(MemberA, MemberB, MemberC);
            setup.Leader(MemberA);
            setup.Unreachable(MemberB);
            setup.A.Tell(new ClusterEvent.MemberRemoved(MemberB.Copy(MemberStatus.Removed), MemberStatus.Exiting));
            setup.Tick();
            ExpectNoMsg(1500);
            setup.Stop();
        }

        [Fact]
        public void Split_Brain_Resolver_must_not_down_when_unreachable_is_already_Down()
        {
            var setup = new SetupKeepMajority(this, TimeSpan.Zero, MemberA.UniqueAddress, null);
            setup.MemberUp(MemberA, MemberB, MemberC);
            setup.Leader(MemberA);
            setup.Unreachable(MemberB.Copy(MemberStatus.Down));
            setup.Tick();
            ExpectNoMsg(1500);
            setup.Stop();
        }

        [Fact]
        public void Split_Brain_Resolver_must_down_minority_partition()
        {
            var setup = new SetupKeepMajority(this, TimeSpan.Zero, MemberA.UniqueAddress, null);
            setup.MemberUp(MemberA, MemberB, MemberC, MemberD, MemberE);
            setup.Leader(MemberA);
            setup.ReachabilityChanged(
                (MemberA, MemberB),
                (MemberC, MemberD));
            setup.Tick();
            setup.ExpectDownCalled(MemberB, MemberD);
            setup.Stop();
        }

        [Fact]
        public void Split_Brain_Resolver_must_keep_partition_with_oldest()
        {
            var setup = new SetupKeepOldest(this, TimeSpan.Zero, MemberA.UniqueAddress, true, null);
            setup.MemberUp(MemberA, MemberB, MemberC, MemberD, MemberE);
            setup.Leader(MemberA);
            setup.ReachabilityChanged(
                (MemberA, MemberB),
                (MemberA, MemberC),
                (MemberE, MemberD));
            setup.Tick();
            setup.ExpectDownCalled(MemberB, MemberC, MemberD);
            setup.Stop();
        }

        [Fact]
        public void Split_Brain_Resolver_must_log_warning_if_N_gt_static_quorum_size_x_2_sub_1()
        {
            var setup = new SetupStaticQuorum(this, TimeSpan.Zero, MemberA.UniqueAddress, 2, null);

            EventFilter.Warning(new Regex("cluster size is \\[4\\].*not add more than \\[3\\]")).ExpectOne(() =>
            {
                setup.MemberUp(MemberA, MemberB, MemberC, MemberD);
            });
            setup.Leader(MemberA);
            setup.Unreachable(MemberC, MemberD);
            setup.Tick();
            // down all
            setup.ExpectDownCalled(MemberA, MemberB, MemberC, MemberD);
            setup.Stop();
        }

        [Fact]
        public void Split_Brain_Resolver_must_down_indirectly_connected_A_B__C___C()
        {
            var setup = new SetupKeepMajority(this, TimeSpan.Zero, MemberA.UniqueAddress, null);
            setup.MemberUp(MemberA, MemberB, MemberC);
            setup.Leader(MemberA);
            setup.ReachabilityChanged(
                (MemberA, MemberB),
                (MemberB, MemberA));
            setup.Tick();
            // keep fully connected memberC
            setup.ExpectDownCalled(MemberA, MemberB);
            setup.Stop();
        }

        [Fact]
        public void Split_Brain_Resolver_must_down_indirectly_connected_when_combined_with_crashed_A_B__D_E__C___D_E()
        {
            var setup = new SetupKeepMajority(this, TimeSpan.Zero, MemberA.UniqueAddress, null);
            setup.MemberUp(MemberA, MemberB, MemberC, MemberD, MemberE);
            setup.Leader(MemberA);
            setup.ReachabilityChanged(
                (MemberA, MemberB),
                (MemberB, MemberA),
                (MemberB, MemberC));
            setup.Tick();
            // keep fully connected memberD, memberE
            // note that crashed memberC is also downed
            setup.ExpectDownCalled(MemberA, MemberB, MemberC);
            setup.Stop();
        }

        [Fact]
        public void Split_Brain_Resolver_must_down_indirectly_connected_when_combined_with_clean_partition_A__B_C__D_E___A()
        {
            // from left side of the partition, memberA, memberB, memberC
            var setup = new SetupKeepMajority(this, TimeSpan.Zero, MemberA.UniqueAddress, null);
            setup.MemberUp(MemberA, MemberB, MemberC, MemberD, MemberE);
            setup.Leader(MemberA);
            // indirectly connected: memberB, memberC
            // clean partition: memberA, memberB, memberC | memeberD, memberE
            setup.ReachabilityChanged(
                (MemberB, MemberC),
                (MemberC, MemberB),
                (MemberA, MemberD),
                (MemberB, MemberD),
                (MemberB, MemberE),
                (MemberC, MemberE));
            setup.Tick();
            // keep fully connected memberA
            // note that memberD and memberE on the other side of the partition are also downed
            setup.ExpectDownCalled(MemberB, MemberC, MemberD, MemberE);
            setup.Stop();


            // from right side of the partition, memberD, memberE
            setup = new SetupKeepMajority(this, TimeSpan.Zero, MemberD.UniqueAddress, null);
            setup.MemberUp(MemberA, MemberB, MemberC, MemberD, MemberE);
            setup.Leader(MemberD);
            // indirectly connected not seen from this side
            // clean partition: memberA, memberB, memberC | memeberD, memberE
            setup.ReachabilityChanged(
                (MemberD, MemberA),
                (MemberD, MemberB),
                (MemberE, MemberB),
                (MemberE, MemberC));
            setup.Tick();
            // Note that memberC is not downed, as on the other side, because those indirectly connected
            // not seen from this side. That outcome is OK.
            setup.ExpectDownCalled(MemberD, MemberE);
            setup.Stop();
        }

        [Fact]
        public void Split_Brain_Resolver_must_down_all_in_self_data_centers()
        {
            var setup = new SetupDownAllNodes(this, TimeSpan.Zero, MemberA.UniqueAddress);
            setup.MemberUp(MemberA, MemberB, MemberC);
            setup.Leader(MemberA);
            setup.Unreachable(MemberA, MemberC);
            setup.Tick();
            setup.ExpectDownCalled(MemberA, MemberB, MemberC);
            setup.Stop();
        }

        [Fact]
        public void Split_Brain_Resolver_must_down_all_when_unstable_scenario_1()
        {
            var setup = new SetupKeepMajority(this, TimeSpan.FromSeconds(2), MemberA.UniqueAddress, null, downAllWhenUnstable: TimeSpan.FromSeconds(1), tickInterval: TimeSpan.FromSeconds(100));
            setup.MemberUp(MemberA, MemberB, MemberC, MemberD, MemberE);
            setup.Leader(MemberA);
            setup.ReachabilityChanged(
                (MemberB, MemberD),
                (MemberB, MemberE));
            setup.Tick();
            setup.ExpectNoDecision(TimeSpan.FromMilliseconds(100));

            Thread.Sleep(1000);
            setup.ReachabilityChanged(
                (MemberB, MemberD));
            setup.Reachable(MemberE);
            setup.Tick();
            setup.ExpectNoDecision(TimeSpan.FromMilliseconds(100));

            Thread.Sleep(1000);
            setup.ReachabilityChanged(
                (MemberB, MemberD),
                (MemberB, MemberE));
            setup.Tick();
            setup.ExpectNoDecision(TimeSpan.FromMilliseconds(100));

            Thread.Sleep(1000);
            setup.ReachabilityChanged(
                (MemberB, MemberD));
            setup.Reachable(MemberE);
            setup.Tick();
            setup.ExpectDownCalled(MemberA, MemberB, MemberC, MemberD, MemberE);
        }

        [Fact]
        public void Split_Brain_Resolver_must_down_all_when_unstable_scenario_2()
        {
            var setup = new SetupKeepMajority(this, TimeSpan.FromSeconds(2), MemberA.UniqueAddress, null, downAllWhenUnstable: TimeSpan.FromMilliseconds(500), tickInterval: TimeSpan.FromSeconds(100));
            setup.MemberUp(MemberA, MemberB, MemberC, MemberD, MemberE);
            setup.Leader(MemberA);
            // E and D are unreachable
            setup.ReachabilityChanged(
                (MemberA, MemberE),
                (MemberB, MemberD),
                (MemberC, MemberD));
            setup.Tick();
            setup.ExpectNoDecision(TimeSpan.FromMilliseconds(100));

            Thread.Sleep(500);
            // E and D are still unreachable
            setup.ReachabilityChanged(
                (MemberA, MemberE),
                (MemberB, MemberD));
            setup.Tick();
            setup.ExpectNoDecision(TimeSpan.FromMilliseconds(100));
            // 600 ms has elapsed

            Thread.Sleep(500);
            setup.ReachabilityChanged(
                (MemberA, MemberE));
            setup.Reachable(MemberD); // reset stableDeadline
            setup.Tick();
            setup.ExpectNoDecision(TimeSpan.FromMilliseconds(100));
            // 1200 ms has elapsed

            Thread.Sleep(500);
            // E and D are unreachable, reset stableDeadline
            setup.ReachabilityChanged(
                (MemberA, MemberE),
                (MemberB, MemberD),
                (MemberC, MemberD));
            setup.Tick();
            setup.ExpectNoDecision(TimeSpan.FromMilliseconds(100));
            // 1800 ms has elapsed

            Thread.Sleep(1000);
            // E and D are still unreachable
            setup.ReachabilityChanged(
                (MemberA, MemberE),
                (MemberB, MemberD));
            setup.Tick();
            // 2800 ms has elapsed and still no stability so downing all
            setup.ExpectDownCalled(MemberA, MemberB, MemberC, MemberD, MemberE);
        }

        [Fact]
        public void Split_Brain_Resolver_must_not_down_all_when_becoming_stable_again()
        {
            var setup = new SetupKeepMajority(this, TimeSpan.FromSeconds(2), MemberA.UniqueAddress, null, downAllWhenUnstable: TimeSpan.FromMilliseconds(1000), tickInterval: TimeSpan.FromSeconds(100));
            setup.MemberUp(MemberA, MemberB, MemberC, MemberD, MemberE);
            setup.Leader(MemberA);
            setup.ReachabilityChanged(
                (MemberB, MemberD),
                (MemberB, MemberE));
            setup.Tick();
            setup.ExpectNoDecision(TimeSpan.FromMilliseconds(100));

            Thread.Sleep(1000);
            setup.ReachabilityChanged(
                (MemberB, MemberD));
            setup.Reachable(MemberE);
            setup.Tick();
            setup.ExpectNoDecision(TimeSpan.FromMilliseconds(100));

            // wait longer than stableAfter
            Thread.Sleep(500);
            setup.Tick();
            setup.ExpectNoDecision(TimeSpan.FromMilliseconds(100));
            setup.ReachabilityChanged();
            setup.Reachable(MemberD);
            Thread.Sleep(500);
            setup.Tick();
            setup.ExpectNoDecision(TimeSpan.FromMilliseconds(100));

            Thread.Sleep(3000);
            setup.Tick();
            setup.ExpectNoDecision(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void Split_Brain_Resolver_must_down_other_side_when_lease_can_be_acquired()
        {
            var setup = new SetupLeaseMajority(this, TimeSpan.Zero, MemberA.UniqueAddress, null, new TestLease(testLeaseSettings, ExtSystem));
            setup.MemberUp(MemberA, MemberB, MemberC);
            setup.Leader(MemberA);
            setup.Unreachable(MemberB);
            setup.TestLease.SetNextAcquireResult(Task.FromResult(true));
            setup.Tick();
            setup.ExpectDownCalled(MemberB);
            setup.Stop();
        }

        [Fact]
        public void Split_Brain_Resolver_must_down_own_side_when_lease_cannot_be_acquired()
        {
            var setup = new SetupLeaseMajority(this, TimeSpan.Zero, MemberA.UniqueAddress, null, new TestLease(testLeaseSettings, ExtSystem));
            setup.MemberUp(MemberA, MemberB, MemberC);
            setup.Leader(MemberA);
            setup.Unreachable(MemberB);
            setup.TestLease.SetNextAcquireResult(Task.FromResult(false));
            setup.Tick();
            setup.ExpectDownCalled(MemberA, MemberC);
            setup.Stop();
        }

        [Fact]
        public void Split_Brain_Resolver_must_down_indirectly_connected_when_lease_can_be_acquired_A_B__C___C()
        {
            var setup = new SetupLeaseMajority(this, TimeSpan.Zero, MemberA.UniqueAddress, null, new TestLease(testLeaseSettings, ExtSystem));
            setup.MemberUp(MemberA, MemberB, MemberC);
            setup.Leader(MemberA);
            setup.ReachabilityChanged(
                (MemberA, MemberB),
                (MemberB, MemberA));
            setup.TestLease.SetNextAcquireResult(Task.FromResult(true));
            setup.Tick();
            // keep fully connected memberC
            setup.ExpectDownCalled(MemberA, MemberB);
            setup.Stop();
        }

        [Fact]
        public void Split_Brain_Resolver_must_down_indirectly_connected_when_lease_cannot_be_acquired_A_B__C___C()
        {
            var setup = new SetupLeaseMajority(this, TimeSpan.Zero, MemberA.UniqueAddress, null, new TestLease(testLeaseSettings, ExtSystem));
            setup.MemberUp(MemberA, MemberB, MemberC);
            setup.Leader(MemberA);
            setup.ReachabilityChanged(
                (MemberA, MemberB),
                (MemberB, MemberA));
            setup.TestLease.SetNextAcquireResult(Task.FromResult(false));
            setup.Tick();
            // all reachable + all indirectly connected
            setup.ExpectDownCalled(MemberA, MemberB, MemberC);
            setup.Stop();
        }

        [Fact]
        public void Split_Brain_Resolver_downing_provider_must_be_loadable_through_the_cluster_extension()
        {
            Cluster.Get(Sys).DowningProvider.Should().BeOfType<SplitBrainResolverProvider>();
        }
    }
}
