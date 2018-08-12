#region copyright
//-----------------------------------------------------------------------
// <copyright file="MultiDcSplitBrainSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode
{
    public class MultiDcSplitBrainConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }
        public RoleName Fifth { get; }

        public MultiDcSplitBrainConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");
            Fifth = Role("fifth");

            CommonConfig = ConfigurationFactory.ParseString(@"
              akka.loglevel = DEBUG
              akka.cluster.debug.verbose-heartbeat-logging = on
              akka.cluster.debug.verbose-gossip-logging = on
              akka.remote.dot-netty.tcp.connection-timeout = 5s # speedup in case of connection issue
              akka.remote.retry-gate-closed-for = 1s
              akka.cluster.multi-data-center {
                failure-detector {
                  acceptable-heartbeat-pause = 4s
                  heartbeat-interval = 1s
                }
              }
              akka.cluster {
                gossip-interval                     = 500ms
                leader-actions-interval             = 1s
                auto-down-unreachable-after         = 1s
              }").WithFallback(MultiNodeClusterSpec.ClusterConfig()); ;
            NodeConfig(new[] { First, Second }, new[]
            {
                ConfigurationFactory.ParseString("akka.cluster.multi-data-center.self-data-center = dc1")
            });
            NodeConfig(new[] { Third, Fourth, Fifth }, new[]
            {
                ConfigurationFactory.ParseString("akka.cluster.multi-data-center.self-data-center = dc2")
            });

            TestTransport = true;
        }
    }

    public class MultiDcSplitBrainSpec : MultiNodeClusterSpec
    {
        private readonly MultiDcSplitBrainConfig _config;
        private int _splits = 0;
        private int _unsplits = 0;

        public MultiDcSplitBrainSpec() : this(new MultiDcSplitBrainConfig()) { }
        protected MultiDcSplitBrainSpec(MultiDcSplitBrainConfig config) : base(config, typeof(MultiDcSplitBrainSpec))
        {
            _config = config;
            DC1 = new[] { First, Second };
            DC2 = new[] { Third, Fourth, Fifth };
        }

        public RoleName First => _config.First;
        public RoleName Second => _config.Second;
        public RoleName Third => _config.Third;
        public RoleName Fourth => _config.Fourth;
        public RoleName Fifth => _config.Fifth;
        public RoleName[] DC1 { get; }
        public RoleName[] DC2 { get; }

        [MultiNodeFact]
        public void Test()
        {
            A_cluster_with_multiple_data_centers_must_be_able_to_form_2_data_centers();
            A_cluster_with_multiple_data_centers_must_be_able_to_have_a_data_center_member_join_while_there_is_inter_data_center_split();
            A_cluster_with_multiple_data_centers_must_be_able_to_have_a_data_center_member_leave_while_there_is_inter_data_center_split();
            A_cluster_with_multiple_data_centers_must_be_able_to_have_a_data_center_member_restart_same_port_and_host_while_there_is_inter_data_center_split();
        }

        private void A_cluster_with_multiple_data_centers_must_be_able_to_form_2_data_centers()
        {
            AwaitClusterUp(First, Second, Third);
        }

        private void A_cluster_with_multiple_data_centers_must_be_able_to_have_a_data_center_member_join_while_there_is_inter_data_center_split() => Within(TimeSpan.FromSeconds(60), () =>
        {
            // introduce a split between data centers
            SplitDataCenters(doNotVerify: new[] { Fourth, Fifth });

            RunOn(() =>
            {
                Cluster.Join(GetAddress(Third));
            }, Fourth);
            EnterBarrier("inter-data-center unreachability");

            // should be able to join and become up since the
            // split is between dc1 and dc2
            RunOn(() =>
            {
                AwaitAssert(() =>
                {
                    ClusterView.Members
                        .Where(m => m.DataCenter == "dc2" && m.Status == MemberStatus.Up)
                        .Select(m => m.Address)
                        .Should().BeEquivalentTo(GetAddress(Third), GetAddress(Fourth));
                });
            }, Third, Fourth);
            EnterBarrier("dc2-join-completed");

            UnsplitDataCenters(notMembers: new[] { Fifth });

            RunOn(() =>
            {
                AwaitAssert(() =>
                {
                    ClusterView.Members
                        .Where(m => m.DataCenter == "dc2" && m.Status == MemberStatus.Up)
                        .Select(m => m.Address)
                        .Should().BeEquivalentTo(GetAddress(Third), GetAddress(Fourth));
                });
            }, DC1);
            EnterBarrier("inter-data-center-split-1-done");
        });

        // fifth is still not a member of the cluster
        private void A_cluster_with_multiple_data_centers_must_be_able_to_have_a_data_center_member_leave_while_there_is_inter_data_center_split() => Within(TimeSpan.FromSeconds(60), () =>
        {
            SplitDataCenters(doNotVerify: new[] { Fifth });

            RunOn(() => Cluster.Leave(GetAddress(Fourth)), Fourth);

            RunOn(() =>
            {
                AwaitAssert(() => ClusterView.Members.Select(m => m.Address).Should().NotContain(GetAddress(Fourth)));
            }, Third);
            EnterBarrier("node-4-left");

            UnsplitDataCenters(notMembers: new[] { Fourth, Fifth });

            RunOn(() =>
            {
                AwaitAssert(() => ClusterView.Members.Select(m => m.Address).Should().NotContain(GetAddress(Fourth)));
            }, First, Second);
            EnterBarrier("inter-data-center-split-2-done");
        });

        private void A_cluster_with_multiple_data_centers_must_be_able_to_have_a_data_center_member_restart_same_port_and_host_while_there_is_inter_data_center_split() => Within(TimeSpan.FromSeconds(60), () =>
        {
            var subscribeProbe = CreateTestProbe();
            RunOn(() =>
            {
                Cluster.Subscribe(subscribeProbe.Ref, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsSnapshot, typeof(ClusterEvent.MemberUp), typeof(ClusterEvent.MemberRemoved));
                subscribeProbe.ExpectMsg<ClusterEvent.CurrentClusterState>();
            }, First, Second, Third, Fifth);
            EnterBarrier("subscribed");

            RunOn(() => Cluster.Get(Sys).Join(GetAddress(Third)), Fifth);

            UniqueAddress fifthOriginalUniqueAddress = null;
            RunOn(() =>
            {
                AwaitAssert(() =>
                {
                    ClusterView.Members
                        .Where(m => m.DataCenter == "dc2" && m.Status == MemberStatus.Up)
                        .Select(m => m.Address)
                        .Should().BeEquivalentTo(GetAddress(Third), GetAddress(Fifth));
                });

                fifthOriginalUniqueAddress = ClusterView.Members.First(m => m.Address == GetAddress(Fifth)).UniqueAddress;
            }, First, Second, Third, Fifth);
            EnterBarrier("fifth-joined");

            SplitDataCenters(doNotVerify: new[] { Fourth });

            RunOn(() => Cluster.Shutdown(), Fifth);

            RunOn(() =>
            {
                AwaitAssert(() =>
                {
                    ClusterView.Members
                        .Where(m => m.DataCenter == "dc2")
                        .Select(m => m.Address)
                        .Should().BeEquivalentTo(GetAddress(Third));
                });
            }, Third);
            EnterBarrier("fifth-removed");

            RunOn(() =>
            {
                // we can't use any multi-jvm test facilities on fifth after this, because have to shutdown
                // actor system to be able to start new with same port
                var thirdAddress = GetAddress(Third);
                EnterBarrier("fifth-waiting-for-termination");
                Sys.WhenTerminated.Wait(Remaining);

                var port = Cluster.SelfAddress.Port.Value;
                var restartedSystem = ActorSystem.Create(Sys.Name, ConfigurationFactory.ParseString($@"
                    akka.remote.dot-netty.tcp.port = {port}
                    akka.coordinated-shutdown.terminate-actor-system = on").WithFallback(Sys.Settings.Config));
                Cluster.Get(restartedSystem).Join(thirdAddress);
                restartedSystem.WhenTerminated.Wait(Remaining);
            }, Fifth);

            // no multi-jvm test facilities on fifth after this
            var remainingRoles = Roles.Where(r => r != Fifth).ToArray();

            RunOn(() => EnterBarrier("fifth-waiting-for-termination"), remainingRoles);

            RunOn(() =>
            {
                foreach (var dc1Node in DC1)
                foreach (var dc2Node in DC2)
                {
                    TestConductor.PassThrough(dc1Node, dc2Node, ThrottleTransportAdapter.Direction.Both).Wait();
                }

                TestConductor.Shutdown(Fifth);
            }, First);

            RunOn(() => EnterBarrier("fifth-restarted"), remainingRoles);

            RunOn(() =>
            {
                AwaitAssert(() => ClusterView.Members
                    .First(m => m.DataCenter == "dc2" && m.Address == fifthOriginalUniqueAddress.Address)
                    .UniqueAddress.Should().NotBe(fifthOriginalUniqueAddress));    // different uid

                subscribeProbe.ExpectMsg<ClusterEvent.MemberUp>(up => up.Member.UniqueAddress == fifthOriginalUniqueAddress);
                subscribeProbe.ExpectMsg<ClusterEvent.MemberRemoved>(up => up.Member.UniqueAddress == fifthOriginalUniqueAddress);
                subscribeProbe.ExpectMsg<ClusterEvent.MemberUp>(up => up.Member.UniqueAddress == fifthOriginalUniqueAddress);
            }, First, Second, Third);

            RunOn(() => EnterBarrier("fifth-re-joined"), remainingRoles);

            RunOn(() =>
            {
                // to shutdown the restartedSystem on fifth
                Cluster.Get(Sys).Leave(fifthOriginalUniqueAddress.Address);
            }, First);

            RunOn(() =>
            {
                AwaitAssert(() => ClusterView.Members
                    .Select(m => m.Address)
                    .Should().BeEquivalentTo(GetAddress(First), GetAddress(Second), GetAddress(Third)));
            }, First, Second, Third);

            RunOn(() => EnterBarrier("restarted-fifth-removed"), remainingRoles);
        });

        private void SplitDataCenters(IEnumerable<RoleName> doNotVerify)
        {
            _splits++;
            var memberNodes = ImmutableHashSet.CreateRange(DC1).Union(DC2).Except(doNotVerify).ToArray();
            var probe = CreateTestProbe();

            RunOn(() =>
            {
                Cluster.Subscribe(probe.Ref, typeof(ClusterEvent.UnreachableDataCenter));
                probe.ExpectMsg<ClusterEvent.CurrentClusterState>();
            }, memberNodes);
            EnterBarrier($"split-{_splits}");

            RunOn(() =>
            {
                foreach (var dc1Node in DC1)
                    foreach (var dc2Node in DC2)
                    {
                        TestConductor.Blackhole(dc1Node, dc2Node, ThrottleTransportAdapter.Direction.Both).Wait();
                    }
            }, First);
            EnterBarrier($"after-split-{_splits}");

            RunOn(() =>
            {
                probe.ExpectMsg<ClusterEvent.UnreachableDataCenter>(TimeSpan.FromSeconds(15));
                Cluster.Unsubscribe(probe.Ref);

                RunOn(() =>
                {
                    AwaitAssert(() => Cluster.State.UnreachableDataCenters.Should().BeEquivalentTo("dc2"));
                }, DC1);
                RunOn(() =>
                {
                    AwaitAssert(() => Cluster.State.UnreachableDataCenters.Should().BeEquivalentTo("dc1"));
                }, DC2);

                Cluster.State.Unreachable.Should().BeEmpty();

            }, memberNodes);
            EnterBarrier($"after-split-verified-{_splits}");
        }

        private void UnsplitDataCenters(IEnumerable<RoleName> notMembers)
        {
            _unsplits++;
            var memberNodes = ImmutableHashSet.CreateRange(DC1).Union(DC2).Except(notMembers).ToArray();
            var probe = CreateTestProbe();

            RunOn(() =>
            {
                Cluster.Subscribe(probe.Ref, typeof(ClusterEvent.ReachableDataCenter));
                probe.ExpectMsg<ClusterEvent.CurrentClusterState>();
            }, memberNodes);
            EnterBarrier($"unsplit-{_unsplits}");

            RunOn(() =>
            {
                foreach (var dc1Node in DC1)
                    foreach (var dc2Node in DC2)
                    {
                        TestConductor.PassThrough(dc1Node, dc2Node, ThrottleTransportAdapter.Direction.Both).Wait();
                    }
            }, First);
            EnterBarrier($"after-unsplit-{_unsplits}");

            RunOn(() =>
            {
                probe.ExpectMsg<ClusterEvent.ReachableDataCenter>(TimeSpan.FromSeconds(25));
                Cluster.Unsubscribe(probe.Ref);

                Log.Debug("Reachable data center received");

                AwaitAssert(() =>
                {
                    Cluster.State.UnreachableDataCenters.Should().BeEmpty();
                    Log.Debug("Cluster state: {0}", Cluster.State);
                });
            }, memberNodes);
            EnterBarrier($"after-unsplit-verified-{_unsplits}");
        }
    }
}