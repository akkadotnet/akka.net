//-----------------------------------------------------------------------
// <copyright file="GossipTombstoneSerializerPropertySpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Akka.Serialization;
using Akka.TestKit;
using CsCheck;
using FluentAssertions;
using Xunit;
using static Akka.Cluster.Tests.GossipTombstoneGenerators;

namespace Akka.Cluster.Tests.Serialization
{
    /// <summary>
    /// P12: proto round trip for gossip carrying removal tombstones.
    ///
    /// A tombstoned node is not a member, so its address is missing from the address table the member
    /// loop builds. Getting that wrong is silent - the tombstone comes back pointing at some other node
    /// - which is why this is a property rather than a handful of examples.
    ///
    /// Replaying a failure: CsCheck prints the failing seed. Pass it back as <c>seed: "..."</c> on the
    /// Sample call, or set the <c>CsCheck_Seed</c> environment variable and rerun.
    /// </summary>
    public class GossipTombstoneSerializerPropertySpecs : AkkaSpec
    {
        private const int Iterations = 1000;

        public GossipTombstoneSerializerPropertySpecs(ITestOutputHelper output)
            : base("akka.actor.provider = cluster", output)
        {
        }

        [Fact(DisplayName = "P12: gossip with tombstones survives a proto round trip intact")]
        public void P12_Gossip_round_trips_with_tombstones()
        {
            var probe = new GossipEnvelope(Node(0), Node(1), Gossip.Empty);
            var serializer = Sys.Serialization.FindSerializerFor(probe);
            serializer.Should().BeOfType<Akka.Cluster.Serialization.ClusterMessageSerializer>();
            var manifest = ((SerializerWithStringManifest)serializer).Manifest(probe);

            // how often the shared host-and-port pair actually landed on opposite sides of the member
            // and tombstone tables
            var sharedHostPortCovered = 0;

            Sides(1).Sample(g =>
            {
                var gossip = g[0];
                var envelope = new GossipEnvelope(Node(0), Node(1), gossip);

                var bytes = serializer.ToBinary(envelope);
                var back = (GossipEnvelope)serializer.FromBinary(bytes, manifest);

                // Describe compares members with status, upNumber, roles and app version; tombstone
                // addresses AND timestamps; reachability records and versions; and the vector clock.
                Describe(back.Gossip).Should().Be(Describe(gossip));

                // spelled out for the case this property exists for: the tombstone key keeps its own UID
                foreach (var kv in gossip.Tombstones)
                {
                    var key = back.Gossip.Tombstones.Keys.Single(k => k.Equals(kv.Key));
                    key.Uid.Should().Be(kv.Key.Uid);
                    key.Address.Should().Be(kv.Key.Address);
                    back.Gossip.Tombstones[key].Should().Be(kv.Value);
                }

                // Node(2) and Node(3) share a host and port and differ only by UID. A serializer that
                // looked a tombstone up by address would hand back the member's UID here.
                var memberAddresses = gossip.Members.Select(m => m.UniqueAddress).ToImmutableHashSet();
                if ((memberAddresses.Contains(Node(2)) && gossip.Tombstones.ContainsKey(Node(3))) ||
                    (memberAddresses.Contains(Node(3)) && gossip.Tombstones.ContainsKey(Node(2))))
                    Interlocked.Increment(ref sharedHostPortCovered);
            }, iter: Iterations, print: Print);

            sharedHostPortCovered.Should().BeGreaterThan(Iterations / 20,
                "the generator must put the shared host-and-port pair on both sides of the member/tombstone split");
        }

        [Fact(DisplayName = "P12: a gossip that tombstones one of its own members still round trips")]
        public void P12_Gossip_round_trips_when_a_tombstone_is_also_a_member()
        {
            // Shape.Adversarial builds gossips that hold a node as a member AND as a tombstone. The Gossip
            // constructor rejects exactly that under AKKA_CLUSTER_ASSERT=on, so the input cannot be built
            // there.
            Assert.SkipWhen(Cluster.IsAssertInvariantsEnabled,
                "AKKA_CLUSTER_ASSERT=on rejects the member-and-tombstone gossip this property is about");

            var probe = new GossipEnvelope(Node(0), Node(1), Gossip.Empty);
            var serializer = Sys.Serialization.FindSerializerFor(probe);
            var manifest = ((SerializerWithStringManifest)serializer).Manifest(probe);

            // how often a tombstone key was already in the address table the member loop built
            var tombstoneWasAlreadyMapped = 0;

            Sides(1, Shape.Adversarial).Sample(g =>
            {
                var gossip = g[0];
                var envelope = new GossipEnvelope(Node(0), Node(1), gossip);

                var bytes = serializer.ToBinary(envelope);
                var back = (GossipEnvelope)serializer.FromBinary(bytes, manifest);

                Describe(back.Gossip).Should().Be(Describe(gossip));

                foreach (var kv in gossip.Tombstones)
                {
                    var key = back.Gossip.Tombstones.Keys.Single(k => k.Equals(kv.Key));
                    key.Uid.Should().Be(kv.Key.Uid);
                    back.Gossip.Tombstones[key].Should().Be(kv.Value);
                }

                // The writer appends a tombstone's address to the shared address table only when the
                // member loop has not already put it there. A tombstone for a node this gossip still holds
                // as a member takes the other arm - it reuses the member's slot instead of appending a
                // second one - and nothing else in the suite reaches that arm.
                if (gossip.Tombstones.Keys.Any(gossip.HasMember))
                    Interlocked.Increment(ref tombstoneWasAlreadyMapped);
            }, iter: Iterations, print: Print);

            tombstoneWasAlreadyMapped.Should().BeGreaterThan(Iterations / 10,
                "Shape.Adversarial must actually produce a tombstone for a node the gossip holds as a member");
        }

        [Fact(DisplayName = "P12: a Welcome carries the same tombstones through a round trip")]
        public void P12_Welcome_round_trips_with_tombstones()
        {
            var probe = new InternalClusterAction.Welcome(Node(0), Gossip.Empty);
            var serializer = Sys.Serialization.FindSerializerFor(probe);
            var manifest = ((SerializerWithStringManifest)serializer).Manifest(probe);

            Sides(1).Sample(g =>
            {
                var welcome = new InternalClusterAction.Welcome(Node(0), g[0]);
                var bytes = serializer.ToBinary(welcome);
                var back = (InternalClusterAction.Welcome)serializer.FromBinary(bytes, manifest);

                Describe(back.Gossip).Should().Be(Describe(g[0]));
            }, iter: Iterations, print: Print);
        }
    }
}
