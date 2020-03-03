//-----------------------------------------------------------------------
// <copyright file="NodeUpSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Remote.TestKit;
using Akka.TestKit;
using Akka.Util;

namespace Akka.Cluster.Tests.MultiNode
{
    public class NodeUpConfig : MultiNodeConfig
    {
        public RoleName First { get; set; }
        public RoleName Second { get; set; }

        public NodeUpConfig()
        {
            First = Role("first");
            Second = Role("second");

            CommonConfig = DebugConfig(false)
                .WithFallback(MultiNodeClusterSpec.ClusterConfigWithFailureDetectorPuppet());
        }
    }

    public class NodeUpSpec : MultiNodeClusterSpec
    {
        private class Listener : UntypedActor
        {
            private readonly AtomicReference<SortedSet<Member>> _unexpected;

            public Listener(AtomicReference<SortedSet<Member>> unexpected)
            {
                _unexpected = unexpected;
            }

            protected override void OnReceive(object message)
            {
                message.Match()
                    .With<ClusterEvent.IMemberEvent>(evt =>
                    {
                        _unexpected.Value.Add(evt.Member);
                    })
                    .With<ClusterEvent.CurrentClusterState>(() =>
                    {
                        // ignore
                    });
            }
        }

        private readonly NodeUpConfig _config;

        public NodeUpSpec() : this(new NodeUpConfig())
        {
        }

        protected NodeUpSpec(NodeUpConfig config) : base(config, typeof(NodeUpSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public void NodeUpSpecs()
        {
            Cluster_node_that_is_joining_another_cluster_must_not_be_able_to_join_node_that_is_not_cluster_member();
            Cluster_node_that_is_joining_another_cluster_must_be_moved_to_up_by_the_leader_after_convergence();
            Cluster_node_that_is_joining_another_cluster_must_be_unaffected_when_joining_again();
        }

        public void Cluster_node_that_is_joining_another_cluster_must_not_be_able_to_join_node_that_is_not_cluster_member()
        {
            RunOn(() =>
            {
                Cluster.Join(GetAddress(_config.Second));
            }, _config.First);
            EnterBarrier("first-join-attempt");

            Thread.Sleep(2000);
            ClusterView.Members.Count.ShouldBe(0);
            EnterBarrier("after-0");
        }

        public void Cluster_node_that_is_joining_another_cluster_must_be_moved_to_up_by_the_leader_after_convergence()
        {
            AwaitClusterUp(_config.First, _config.Second);
            EnterBarrier("after-1");
        }

        public void Cluster_node_that_is_joining_another_cluster_must_be_unaffected_when_joining_again()
        {
            var unexpected = new AtomicReference<SortedSet<Member>>(new SortedSet<Member>());
            Cluster.Subscribe(Sys.ActorOf(Props.Create(() => new Listener(unexpected))), new[]
            {
                typeof(ClusterEvent.IMemberEvent)
            });
            EnterBarrier("listener-registered");

            RunOn(() =>
            {
                Cluster.Join(GetAddress(_config.First));
            }, _config.Second);
            EnterBarrier("joined-again");

            foreach (var n in Enumerable.Range(1, 20))
            {
                Thread.Sleep(Dilated(TimeSpan.FromMilliseconds(100)));
                unexpected.Value.Count.ShouldBe(0);
                ClusterView.Members.All(c => c.Status == MemberStatus.Up).ShouldBeTrue();
            }
        }
    }
}
