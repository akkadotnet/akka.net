//-----------------------------------------------------------------------
// <copyright file="ClusterMetricsMessageSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Metrics.Helpers;
using Akka.Cluster.Metrics.Serialization;
using Akka.Cluster.Tests;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Address = Akka.Actor.Address;

namespace Akka.Cluster.Metrics.Tests
{
    public class ClusterMetricsMessageSerializerSpec : AkkaSpec
    {
        private readonly ClusterMetricsMessageSerializer _serializer;
        
        private readonly Member _a1 = TestMember.Create(new Address("akka", "sys", "a", 2552), MemberStatus.Joining, ImmutableHashSet<string>.Empty);
        private readonly Member _b1 = TestMember.Create(new Address("akka", "sys", "b", 2552), MemberStatus.Up, ImmutableHashSet<string>.Empty.Add("r1"));
        private Member _c1 = TestMember.Create(new Address("akka", "sys", "c", 2552), MemberStatus.Leaving, ImmutableHashSet<string>.Empty.Add("r2"));
        private Member _d1 = TestMember.Create(new Address("akka", "sys", "d", 2552), MemberStatus.Exiting, ImmutableHashSet<string>.Empty.Add("r1").Add("r2"));
        private Member _e1 = TestMember.Create(new Address("akka", "sys", "e", 2552), MemberStatus.Down, ImmutableHashSet<string>.Empty.Add("r3"));
        private Member _f1 = TestMember.Create(new Address("akka", "sys", "f", 2552), MemberStatus.Removed, ImmutableHashSet<string>.Empty.Add("r2").Add("r3"));
        
        public ClusterMetricsMessageSerializerSpec()
            : base("akka.actor.provider = cluster")
        {
            _serializer = new ClusterMetricsMessageSerializer(Sys as ExtendedActorSystem);
        }

        [Fact]
        public void Cluster_messages_should_be_serializable()
        {
            var metrics = ImmutableHashSet<NodeMetrics>.Empty;
            metrics = metrics.Add(new NodeMetrics(_a1.Address, 4711, new []
            {
                new NodeMetrics.Types.Metric("foo", 1.2, Option<NodeMetrics.Types.EWMA>.None)
            }));
            metrics = metrics.Add(new NodeMetrics(_b1.Address, 4712, new []
            {
                new NodeMetrics.Types.Metric("foo", 2.1, new NodeMetrics.Types.EWMA(value: 100, alpha: 0.18)), 
                new NodeMetrics.Types.Metric("bar1", double.MaxValue, Option<NodeMetrics.Types.EWMA>.None), 
                new NodeMetrics.Types.Metric("bar2",  float.MaxValue, Option<NodeMetrics.Types.EWMA>.None), 
                new NodeMetrics.Types.Metric("bar3",  int.MaxValue, Option<NodeMetrics.Types.EWMA>.None), 
                new NodeMetrics.Types.Metric("bar4",  long.MaxValue, Option<NodeMetrics.Types.EWMA>.None), 
            }));
            
            var gossip = new MetricsGossip(metrics);
            
            CheckSerialization(new MetricsGossipEnvelope(_a1.Address, gossip, true));
        }

        [Fact]
        public void AdaptiveLoadBalancingPool_Should_be_serializable()
        {
            var simplePool = new AdaptiveLoadBalancingPool();
            CheckSerialization(simplePool);
            
            var complicatedPool = new AdaptiveLoadBalancingPool(
                metricsSelector: new MixMetricsSelector(new CapacityMetricsSelector[]
                {
                    CpuMetricsSelector.Instance, 
                    MemoryMetricsSelector.Instance, 
                }.ToImmutableArray()),
                nrOfInstances: 7,
                routerDispatcher:"my-dispatcher",
                usePoolDispatcher: true
            );
            CheckSerialization(complicatedPool);
        }

        private void CheckSerialization(object obj)
        {
            var blob = _serializer.ToBinary(obj);
            var @ref = _serializer.FromBinary(blob, _serializer.Manifest(obj));
            @ref.ShouldBeEquivalentTo(obj);
        }
    }
}
