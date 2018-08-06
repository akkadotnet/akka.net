#region copyright
//-----------------------------------------------------------------------
// <copyright file="MultiDcHeartbeatTakingOverSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Pattern;
using Akka.Remote.TestKit;
using Akka.TestKit;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Akka.Cluster.Tests.MultiNode
{
    public class MultiDcHeartbeatTakingOverConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }
        public RoleName Fifth { get; }

        public MultiDcHeartbeatTakingOverConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");
            Fifth = Role("fifth");

            CommonConfig = ConfigurationFactory.ParseString(@"
            akka {
              test.testkit.debug = on
              actor.provider = cluster
              loglevel = DEBUG
              remote.log-remote-lifecycle-events = off
              cluster {
                debug.verbose-heartbeat-logging = off
                multi-data-center {
                  cross-data-center-connections = 2
                }
              }
            }").WithFallback(MultiNodeClusterSpec.ClusterConfig());

            NodeConfig(new[] { First, Second, Third }, new[]
            {
                ConfigurationFactory.ParseString("akka.cluster.multi-data-center.self-data-center = alpha")
            });

            NodeConfig(new[] { Fourth, Fifth }, new[]
            {
                ConfigurationFactory.ParseString("akka.cluster.multi-data-center.self-data-center = beta")
            });
        }
    }

    public class MultiDcHeartbeatTakingOverSpec : MultiNodeClusterSpec
    {
        private class TestState
        {
            public ActorSelection SelectCrossDcHeartbeatSender { get; }
            public ImmutableSortedSet<Member> ExpectedAlphaHeartbeaterNodes { get; }
            public IImmutableSet<RoleName> ExpectedAlphaHeartbeaterRoles { get; }
            public ImmutableSortedSet<Member> ExpectedBetaHeartbeaterNodes { get; }
            public IImmutableSet<RoleName> ExpectedBetaHeartbeaterRoles { get; }
            public IImmutableSet<RoleName> ExpectedNoActiveHeartbeatSenderRoles { get; }

            public TestState(ActorSelection selectCrossDcHeartbeatSender, ImmutableSortedSet<Member> expectedAlphaHeartbeaterNodes, IImmutableSet<RoleName> expectedAlphaHeartbeaterRoles, ImmutableSortedSet<Member> expectedBetaHeartbeaterNodes, IImmutableSet<RoleName> expectedBetaHeartbeaterRoles, IImmutableSet<RoleName> expectedNoActiveHeartbeatSenderRoles)
            {
                SelectCrossDcHeartbeatSender = selectCrossDcHeartbeatSender;
                ExpectedAlphaHeartbeaterNodes = expectedAlphaHeartbeaterNodes;
                ExpectedAlphaHeartbeaterRoles = expectedAlphaHeartbeaterRoles;
                ExpectedBetaHeartbeaterNodes = expectedBetaHeartbeaterNodes;
                ExpectedBetaHeartbeaterRoles = expectedBetaHeartbeaterRoles;
                ExpectedNoActiveHeartbeatSenderRoles = expectedNoActiveHeartbeatSenderRoles;
            }
        }

        private readonly MultiDcHeartbeatTakingOverConfig _config;
        private TestState _status;
        private TestState Status => _status ?? throw new IllegalStateException("Test status not initialized");

        public MultiDcHeartbeatTakingOverSpec() : this(new MultiDcHeartbeatTakingOverConfig()) { }
        protected MultiDcHeartbeatTakingOverSpec(MultiDcHeartbeatTakingOverConfig config) : base(config, typeof(MultiDcHeartbeatTakingOverSpec))
        {
            _config = config;
        }

        public RoleName First => _config.First;
        public RoleName Second => _config.Second;
        public RoleName Third => _config.Third;
        public RoleName Fourth => _config.Fourth;
        public RoleName Fifth => _config.Fifth;

        [MultiNodeFact]
        public void Test()
        {
            A_2_DC_cluster_must_collect_information_on_oldest_nodes();
            A_2_DC_cluster_must_be_healthy();
            A_2_DC_cluster_must_other_node_must_become_oldest_when_current_DC_oldest_Leaves();
        }

        private void A_2_DC_cluster_must_collect_information_on_oldest_nodes()
        {
            // allow all nodes to join:
            AwaitClusterUp(Roles.ToArray());

            RefreshOldestMemberHeartbeatStatuses();

            Log.Info($"expectedAlphaHeartbeaterNodes = ${string.Join(", ", Status.ExpectedAlphaHeartbeaterNodes.Select(n => n.Address.Port.Value))}");
            Log.Info($"expectedBetaHeartbeaterNodes = ${string.Join(", ", Status.ExpectedBetaHeartbeaterNodes.Select(n => n.Address.Port.Value))}");
            Log.Info($"expectedNoActiveHeartbeatSenderRoles = ${string.Join(", ", Status.ExpectedNoActiveHeartbeatSenderRoles.Select(n => GetAddress(n).Port.Value))}");

            Status.ExpectedAlphaHeartbeaterRoles.Count.Should().Be(2);
            Status.ExpectedBetaHeartbeaterRoles.Count.Should().Be(2);

            EnterBarrier("found-expectations");
        }

        private void A_2_DC_cluster_must_be_healthy()
        {
            var observer = CreateTestProbe("alpha-observer");
            var s = Status;
            RunOn(() =>
            {
                AwaitAssert(() =>
                {
                    s.SelectCrossDcHeartbeatSender.Tell(new CrossDcHeartbeatSender.ReportStatus(), observer.Ref);
                    observer.ExpectMsg<CrossDcHeartbeatSender.MonitoringActive>();
                });
            }, s.ExpectedAlphaHeartbeaterRoles.ToArray());

            RunOn(() =>
            {
                AwaitAssert(() =>
                {
                    s.SelectCrossDcHeartbeatSender.Tell(new CrossDcHeartbeatSender.ReportStatus(), observer.Ref);
                    observer.ExpectMsg<CrossDcHeartbeatSender.MonitoringActive>();
                });
            }, s.ExpectedBetaHeartbeaterRoles.ToArray());

            RunOn(() =>
            {
                AwaitAssert(() =>
                {
                    s.SelectCrossDcHeartbeatSender.Tell(new CrossDcHeartbeatSender.ReportStatus(), observer.Ref);
                    observer.ExpectMsg<CrossDcHeartbeatSender.MonitoringDormant>();
                });
            }, s.ExpectedNoActiveHeartbeatSenderRoles.ToArray());

            EnterBarrier("sunny-weather-done");
        }

        private void A_2_DC_cluster_must_other_node_must_become_oldest_when_current_DC_oldest_Leaves()
        {
            var observer = CreateTestProbe("alpha-observer-prime");

            // we leave one of the current oldest nodes of the `alpha` DC,
            // since it has 3 members the "not yet oldest" one becomes oldest and should start monitoring across datacenter
            var preLeaveOldestAlphaRole = Status.ExpectedAlphaHeartbeaterRoles.First();
            var preLeaveOldestAlphaAddress = Status.ExpectedAlphaHeartbeaterNodes.First(n => n.Address.Port == GetAddress(preLeaveOldestAlphaRole).Port).Address;

            RunOn(() =>
            {
                Log.Info($"Leaving: {preLeaveOldestAlphaAddress}");
                Cluster.Leave(Cluster.SelfAddress);
            }, preLeaveOldestAlphaRole);

            AwaitMemberRemoved(preLeaveOldestAlphaAddress);
            EnterBarrier("wat");

            // refresh our view about who is currently monitoring things in alpha:
            RefreshOldestMemberHeartbeatStatuses();

            EnterBarrier("after-alpha-monitoring-node-left");

            var expectedAlphaMonitoringNodesAfterLeaving = TakeNOldestMembers("alpha", 3).Where(m => m.Status != MemberStatus.Exiting).ToImmutableHashSet();
            RunOn(() =>
            {
                AwaitAssert(() =>
                {
                    Status.SelectCrossDcHeartbeatSender.Tell(new CrossDcHeartbeatSender.ReportStatus(), observer.Ref);
                    observer.ExpectMsg<CrossDcHeartbeatSender.MonitoringActive>(TimeSpan.FromSeconds(5));
                    Log.Info($"Got confirmation from {observer.LastSender} that it is actively monitoring now");

                }, TimeSpan.FromSeconds(20));
            }, MembersAsRoles(expectedAlphaMonitoringNodesAfterLeaving).ToArray());
            EnterBarrier("confirmed-heartbeating-take-over");
        }

        private void RefreshOldestMemberHeartbeatStatuses()
        {
            var crossDcHeartbeatSenderPath = "/system/cluster/core/daemon/crossDcHeartbeatSender";
            var selectCrossDcHeartbeatSender = Sys.ActorSelection(crossDcHeartbeatSenderPath);

            var expectedAlphaHeartbeaterNodes = TakeNOldestMembers(dataCenter: "alpha", n: 2);
            var expectedAlphaHeartbeaterRoles = MembersAsRoles(expectedAlphaHeartbeaterNodes);

            var expectedBetaHeartbeaterNodes = TakeNOldestMembers(dataCenter: "beta", n: 2);
            var expectedBetaHeartbeaterRoles = MembersAsRoles(expectedBetaHeartbeaterNodes);

            var expectedNoActiveHeartbeatSenderRoles = Roles.ToImmutableHashSet()
                .Except(expectedAlphaHeartbeaterRoles)
                .Except(expectedBetaHeartbeaterRoles);

            _status = new TestState(
                selectCrossDcHeartbeatSender: selectCrossDcHeartbeatSender,
                expectedAlphaHeartbeaterNodes: expectedAlphaHeartbeaterNodes,
                expectedAlphaHeartbeaterRoles: expectedAlphaHeartbeaterRoles,
                expectedBetaHeartbeaterNodes: expectedBetaHeartbeaterNodes,
                expectedBetaHeartbeaterRoles: expectedBetaHeartbeaterRoles,
                expectedNoActiveHeartbeatSenderRoles: expectedNoActiveHeartbeatSenderRoles);
        }

        /// <summary>
        /// INTERNAL API
        /// Returns `Up` (or in "later" status, like Leaving etc, but never `Joining` or `WeaklyUp`) members,
        /// sorted by Member.ageOrdering (from oldest to youngest). This restriction on status is needed to
        /// strongly guaratnee the order of "oldest" members, as they're linearized by the order in which they become Up
        /// (since marking that transition is a Leader action).
        /// </summary>
        private ImmutableSortedSet<Member> MembersByAge(string dataCenter) =>
            ImmutableSortedSet.Create<Member>(Member.AgeOrdering)
                .Union(Cluster.State.Members.Where(m =>
                    m.DataCenter == dataCenter && m.Status != MemberStatus.Joining &&
                    m.Status != MemberStatus.WeaklyUp));

        private ImmutableSortedSet<Member> TakeNOldestMembers(string dataCenter, int n) =>
            MembersByAge(dataCenter).Take(n).ToImmutableSortedSet(Member.AgeOrdering);

        private IImmutableSet<RoleName> MembersAsRoles(IImmutableSet<Member> members)
        {
            var res = members.Select(m => RoleName(m.Address)).ToImmutableHashSet();
            if (res.Count != members.Count)
                throw new ArgumentException($"Not all members were converted to roles! Got: [{string.Join(", ", members)}], found [{string.Join(", ", res)}]");
            return res;
        }
    }
}