//-----------------------------------------------------------------------
// <copyright file="ClusterMetricsMessageSerializerNestedSelectorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Metrics.Serialization;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Metrics.Tests
{
    /// <summary>
    /// A selector Akka.Cluster.Metrics never binds in its own reference.conf, so it always falls back to the
    /// default (json) serializer, regardless of whether the Cluster.Metrics rows are loaded.
    /// </summary>
    internal sealed class CustomMetricsSelector : IMetricsSelector
    {
        public static readonly CustomMetricsSelector Instance = new CustomMetricsSelector();

        public IImmutableDictionary<Address, int> Weights(IImmutableSet<NodeMetrics> nodeMetrics)
            => ImmutableDictionary<Address, int>.Empty;
    }

    /// <summary>
    /// Regression coverage for the nested <see cref="IMetricsSelector"/> manifest bug in
    /// <see cref="ClusterMetricsMessageSerializer.MetricsSelectorToProto"/>.
    ///
    /// Akka.Cluster.Metrics' own reference.conf binds <see cref="CpuMetricsSelector"/>, <see cref="MemoryMetricsSelector"/>
    /// and <see cref="Akka.Cluster.Metrics.MixMetricsSelector"/> directly to <see cref="ClusterMetricsMessageSerializer"/>
    /// - which is exactly what happens in any real deployment that touches the ClusterMetrics extension (it injects
    /// those rows via <see cref="ClusterMetrics.DefaultConfig"/>). Once those rows are loaded, a nested selector
    /// inside an <see cref="Akka.Cluster.Metrics.AdaptiveLoadBalancingPool"/> resolves back to
    /// <see cref="ClusterMetricsMessageSerializer"/> itself, so its manifest must be that same serializer's own
    /// short code (e.g. "d" for Cpu) rather than a type-qualified name the serializer's own <c>FromBinary</c>
    /// switch doesn't recognize.
    ///
    /// <see cref="Akka.Cluster.Metrics.AdaptiveLoadBalancingGroup"/> is not bound in reference.conf, so its nested
    /// selector always round-trips through the plain json surrogate path - included here purely for parity, not
    /// because it exercises the bug.
    /// </summary>
    public class ClusterMetricsMessageSerializerNestedSelectorSpec : AkkaSpec
    {
        private static readonly Config MetricsConfig = ClusterMetrics.DefaultConfig();

        public ClusterMetricsMessageSerializerNestedSelectorSpec(ITestOutputHelper output)
            : base(MetricsConfig, output)
        {
        }

        private object RoundTrip(object message)
        {
            var serializer = Sys.Serialization.FindSerializerFor(message);
            var bytes = serializer.ToBinary(message);
            var manifest = Akka.Serialization.Serialization.ManifestFor(serializer, message);
            return Sys.Serialization.Deserialize(bytes, serializer.Identifier, manifest);
        }

        private void AssertPoolRoundTrips(IMetricsSelector selector, Type expectedSelectorType)
        {
            var pool = new AdaptiveLoadBalancingPool(
                metricsSelector: selector,
                nrOfInstances: 5,
                routerDispatcher: "my-dispatcher",
                usePoolDispatcher: true);

            var result = RoundTrip(pool).Should().BeOfType<AdaptiveLoadBalancingPool>().Subject;

            result.Should().BeEquivalentTo(pool);
            result.MetricsSelector.Should().BeOfType(expectedSelectorType);
        }

        private void AssertGroupRoundTrips(IMetricsSelector selector, Type expectedSelectorType)
        {
            var group = new AdaptiveLoadBalancingGroup(
                metricsSelector: selector,
                paths: new[] { "/user/routee" },
                routerDispatcher: "my-dispatcher");

            var result = RoundTrip(group).Should().BeOfType<AdaptiveLoadBalancingGroup>().Subject;

            var expectedSurrogate = (AdaptiveLoadBalancingGroup.AdaptiveLoadBalancingGroupSurrogate)group.ToSurrogate(Sys);
            var actualSurrogate = (AdaptiveLoadBalancingGroup.AdaptiveLoadBalancingGroupSurrogate)result.ToSurrogate(Sys);

            actualSurrogate.MetricsSelector.Should().BeOfType(expectedSelectorType);
            actualSurrogate.Paths.Should().BeEquivalentTo(expectedSurrogate.Paths);
            actualSurrogate.RouterDispatcher.Should().Be(expectedSurrogate.RouterDispatcher);
        }

        [Fact(DisplayName = "Should_round_trip_AdaptiveLoadBalancingPool_When_the_selector_is_CpuMetricsSelector")]
        public void Should_round_trip_AdaptiveLoadBalancingPool_When_the_selector_is_CpuMetricsSelector()
            => AssertPoolRoundTrips(CpuMetricsSelector.Instance, typeof(CpuMetricsSelector));

        [Fact(DisplayName = "Should_round_trip_AdaptiveLoadBalancingPool_When_the_selector_is_MemoryMetricsSelector")]
        public void Should_round_trip_AdaptiveLoadBalancingPool_When_the_selector_is_MemoryMetricsSelector()
            => AssertPoolRoundTrips(MemoryMetricsSelector.Instance, typeof(MemoryMetricsSelector));

        [Fact(DisplayName = "Should_round_trip_AdaptiveLoadBalancingPool_When_the_selector_is_a_non_default_MixMetricsSelector")]
        public void Should_round_trip_AdaptiveLoadBalancingPool_When_the_selector_is_a_non_default_MixMetricsSelector()
            => AssertPoolRoundTrips(
                new MixMetricsSelector(ImmutableArray.Create<CapacityMetricsSelector>(CpuMetricsSelector.Instance)),
                typeof(MixMetricsSelector));

        [Fact(DisplayName = "Should_round_trip_AdaptiveLoadBalancingPool_When_the_selector_is_a_custom_json_backed_selector")]
        public void Should_round_trip_AdaptiveLoadBalancingPool_When_the_selector_is_a_custom_json_backed_selector()
            => AssertPoolRoundTrips(CustomMetricsSelector.Instance, typeof(CustomMetricsSelector));

        [Fact(DisplayName = "Should_round_trip_AdaptiveLoadBalancingGroup_When_the_selector_is_CpuMetricsSelector")]
        public void Should_round_trip_AdaptiveLoadBalancingGroup_When_the_selector_is_CpuMetricsSelector()
            => AssertGroupRoundTrips(CpuMetricsSelector.Instance, typeof(CpuMetricsSelector));

        [Fact(DisplayName = "Should_round_trip_AdaptiveLoadBalancingGroup_When_the_selector_is_MemoryMetricsSelector")]
        public void Should_round_trip_AdaptiveLoadBalancingGroup_When_the_selector_is_MemoryMetricsSelector()
            => AssertGroupRoundTrips(MemoryMetricsSelector.Instance, typeof(MemoryMetricsSelector));

        [Fact(DisplayName = "Should_round_trip_AdaptiveLoadBalancingGroup_When_the_selector_is_a_non_default_MixMetricsSelector")]
        public void Should_round_trip_AdaptiveLoadBalancingGroup_When_the_selector_is_a_non_default_MixMetricsSelector()
            => AssertGroupRoundTrips(
                new MixMetricsSelector(ImmutableArray.Create<CapacityMetricsSelector>(MemoryMetricsSelector.Instance)),
                typeof(MixMetricsSelector));

        [Fact(DisplayName = "Should_round_trip_AdaptiveLoadBalancingGroup_When_the_selector_is_a_custom_json_backed_selector")]
        public void Should_round_trip_AdaptiveLoadBalancingGroup_When_the_selector_is_a_custom_json_backed_selector()
            => AssertGroupRoundTrips(CustomMetricsSelector.Instance, typeof(CustomMetricsSelector));
    }
}
