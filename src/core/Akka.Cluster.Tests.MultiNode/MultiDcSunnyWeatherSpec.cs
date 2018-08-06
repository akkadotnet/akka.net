#region copyright
//-----------------------------------------------------------------------
// <copyright file="MultiDcSunnyWeatherSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode
{
    public class MultiDcSunnyWeatherConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }
        public RoleName Fifth { get; }

        public MultiDcSunnyWeatherConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");
            Fifth = Role("fifth");

            CommonConfig = ConfigurationFactory.ParseString(@"
            akka {
              actor.provider = cluster
              loglevel = INFO
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

    public class MultiDcSunnyWeatherSpec : MultiNodeClusterSpec
    {
        private readonly MultiDcSunnyWeatherConfig _config;

        protected MultiDcSunnyWeatherSpec(MultiDcSunnyWeatherConfig config) : base(config, typeof(MultiDcSunnyWeatherSpec))
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
            A_normal_cluster_must_be_healthy();
            A_normal_cluster_must_never_heartbeat_to_itself_or_members_of_same_as_its_own_data_center();
        }

        private void A_normal_cluster_must_be_healthy()
        {
            var observer = CreateTestProbe("alpha-observer");

            // allow all nodes to join:
            AwaitClusterUp(Roles.ToArray());

            var crossDcHeartbeatSenderPath = "/system/cluster/core/daemon/crossDcHeartbeatSender";
            var selectCrossDcHeartbeatSender = Sys.ActorSelection(crossDcHeartbeatSenderPath);

            var expectedAlphaHeartbeaterNodes = TakeNOldestMembers(dataCenter: "alpha", n: 2);
            var expectedAlphaHeartbeaterRoles = MembersAsRoles(expectedAlphaHeartbeaterNodes);

            var expectedBetaHeartbeaterNodes = TakeNOldestMembers(dataCenter: "beta", n: 2);
            var expectedBetaHeartbeaterRoles = MembersAsRoles(expectedBetaHeartbeaterNodes);

            var expectedNoActiveHeartbeatSenderRoles = Roles.ToImmutableHashSet()
                .Except(expectedAlphaHeartbeaterRoles)
                .Except(expectedBetaHeartbeaterRoles);

            EnterBarrier("found-expectations");

            Log.Info($"expectedAlphaHeartbeaterNodes = ${string.Join(", ", expectedAlphaHeartbeaterNodes.Select(n => n.Address.Port.Value))}");
            Log.Info($"expectedBetaHeartbeaterNodes = ${string.Join(", ", expectedBetaHeartbeaterNodes.Select(n => n.Address.Port.Value))}");
            Log.Info($"expectedNoActiveHeartbeatSenderRoles = ${string.Join(", ", expectedNoActiveHeartbeatSenderRoles.Select(n => GetAddress(n).Port.Value))}");

            expectedAlphaHeartbeaterRoles.Count.Should().Be(2);
            expectedBetaHeartbeaterRoles.Count.Should().Be(2);

            RunOn(() =>
            {
                selectCrossDcHeartbeatSender.Tell(new CrossDcHeartbeatSender.ReportStatus(), observer.Ref);
                var status = observer.ExpectMsg<CrossDcHeartbeatSender.MonitoringActive>(TimeSpan.FromSeconds(5));
            }, expectedAlphaHeartbeaterRoles.ToArray());

            RunOn(() =>
            {
                selectCrossDcHeartbeatSender.Tell(new CrossDcHeartbeatSender.ReportStatus(), observer.Ref);
                var status = observer.ExpectMsg<CrossDcHeartbeatSender.MonitoringActive>(TimeSpan.FromSeconds(5));
            }, expectedBetaHeartbeaterRoles.ToArray());

            RunOn(() =>
            {
                selectCrossDcHeartbeatSender.Tell(new CrossDcHeartbeatSender.ReportStatus(), observer.Ref);
                var status = observer.ExpectMsg<CrossDcHeartbeatSender.MonitoringDormant>(TimeSpan.FromSeconds(5));
            }, expectedNoActiveHeartbeatSenderRoles.ToArray());

            EnterBarrier("done");
        }

        private void A_normal_cluster_must_never_heartbeat_to_itself_or_members_of_same_as_its_own_data_center()
        {
            var observer = CreateTestProbe("alpha-observer");

            var crossDcHeartbeatSenderPath = "/system/cluster/core/daemon/crossDcHeartbeatSender";
            var selectCrossDcHeartbeatSender = Sys.ActorSelection(crossDcHeartbeatSenderPath);

            EnterBarrier("checking-activeReceivers");

            selectCrossDcHeartbeatSender.Tell(new CrossDcHeartbeatSender.ReportStatus(), observer.Ref);
            var report = observer.ExpectMsg<CrossDcHeartbeatSender.IMonitoringStateReport>(TimeSpan.FromSeconds(5));

            switch (report)
            {
                case CrossDcHeartbeatSender.MonitoringDormant d: break; // ok
                case CrossDcHeartbeatSender.MonitoringActive a:
                    // must not heartbeat myself
                    a.State.ActiveReceivers.Should().NotContain(Cluster.SelfUniqueAddress);
                    // not any of the members in the same datacenter; it's "cross-dc" after all
                    var myDataCenterMembers = a.State.State != null ? a.State.State[Cluster.SelfDataCenter] : ImmutableSortedSet<Member>.Empty;
                    foreach (var member in myDataCenterMembers)
                    {
                        a.State.ActiveReceivers.Should().NotContain(member.UniqueAddress);
                    }
                    break;
            }

            EnterBarrier("done-checking-activeReceivers");
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