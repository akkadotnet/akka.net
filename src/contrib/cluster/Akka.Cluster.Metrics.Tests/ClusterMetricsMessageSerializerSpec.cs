//-----------------------------------------------------------------------
// <copyright file="ClusterMetricsMessageSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Metrics.Helpers;
using Akka.Cluster.Metrics.Serialization;
using Akka.Cluster.Tests;
using Akka.Configuration;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Address = Akka.Actor.Address;

namespace Akka.Cluster.Metrics.Tests
{
    public class ClusterMetricsMessageSerializerSpec : AkkaSpec
    {
        /// <summary>
        /// A selector Akka.Cluster.Metrics never binds in its own reference.conf, so it always falls back to the
        /// default (json) serializer - unaffected by <see cref="ClusterMetricsMessageSerializer"/>'s own manifest
        /// handling, included here purely to confirm that path still round-trips.
        /// </summary>
        private sealed class CustomMetricsSelector : IMetricsSelector
        {
            public static readonly CustomMetricsSelector Instance = new CustomMetricsSelector();

            public IImmutableDictionary<Address, int> Weights(IImmutableSet<NodeMetrics> nodeMetrics)
                => ImmutableDictionary<Address, int>.Empty;
        }

        private readonly ClusterMetricsMessageSerializer _serializer;

        private readonly Member _a1 = TestMember.Create(new Address("akka", "sys", "a", 2552), MemberStatus.Joining, ImmutableHashSet<string>.Empty);
        private readonly Member _b1 = TestMember.Create(new Address("akka", "sys", "b", 2552), MemberStatus.Up, ImmutableHashSet<string>.Empty.Add("r1"));
        private Member _c1 = TestMember.Create(new Address("akka", "sys", "c", 2552), MemberStatus.Leaving, ImmutableHashSet<string>.Empty.Add("r2"));
        private Member _d1 = TestMember.Create(new Address("akka", "sys", "d", 2552), MemberStatus.Exiting, ImmutableHashSet<string>.Empty.Add("r1").Add("r2"));
        private Member _e1 = TestMember.Create(new Address("akka", "sys", "e", 2552), MemberStatus.Down, ImmutableHashSet<string>.Empty.Add("r3"));
        private Member _f1 = TestMember.Create(new Address("akka", "sys", "f", 2552), MemberStatus.Removed, ImmutableHashSet<string>.Empty.Add("r2").Add("r3"));

        public ClusterMetricsMessageSerializerSpec()
            // Loads the real Cluster.Metrics reference.conf rows on top of the base config - the same thing any
            // real deployment gets once the ClusterMetrics extension is active - so CpuMetricsSelector,
            // MemoryMetricsSelector and MixMetricsSelector resolve back to ClusterMetricsMessageSerializer itself,
            // the way they do in production, instead of falling back to json.
            : base(ConfigurationFactory.ParseString("akka.actor.provider = cluster").WithFallback(ClusterMetrics.DefaultConfig()))
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

            // A non-default MixMetricsSelector: with the Cluster.Metrics rows loaded (see the ctor), this nested
            // selector resolves to ClusterMetricsMessageSerializer itself, so its manifest must be that same
            // serializer's own short code, not a type-qualified name.
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

            // Same bug, simpler shape: a nested CpuMetricsSelector/MemoryMetricsSelector (also bound directly to
            // ClusterMetricsMessageSerializer in reference.conf) hits the exact same manifest mismatch.
            var cpuPool = new AdaptiveLoadBalancingPool(metricsSelector: CpuMetricsSelector.Instance, nrOfInstances: 3);
            CheckSerialization(cpuPool);

            var memoryPool = new AdaptiveLoadBalancingPool(metricsSelector: MemoryMetricsSelector.Instance, nrOfInstances: 3);
            CheckSerialization(memoryPool);

            // A selector Cluster.Metrics doesn't bind falls back to json instead, unaffected by the bug above.
            var customSelectorPool = new AdaptiveLoadBalancingPool(metricsSelector: CustomMetricsSelector.Instance, nrOfInstances: 3);
            CheckSerialization(customSelectorPool);
        }

        private void CheckSerialization(object obj)
        {
            var blob = _serializer.ToBinary(obj);
            var @ref = _serializer.FromBinary(blob, _serializer.Manifest(obj));
            @ref.Should().BeEquivalentTo(obj);
        }
    }
}
