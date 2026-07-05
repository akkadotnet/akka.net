//-----------------------------------------------------------------------
// <copyright file="AssociationRegistrySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.Artery;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Unit tests for <see cref="AssociationRegistry"/>: address-keyed, CAS-materialized
    /// association storage plus the uid reverse index. See
    /// <c>openspec/changes/artery-tcp-remoting/design.md</c>
    /// ("Handshake + association/UID (gate G2)") and the reverse-index policy documented on
    /// <see cref="AssociationRegistry"/> itself.
    /// </summary>
    public class AssociationRegistrySpec
    {
        private static readonly Address AddressA = new("akka", "sys-a", "host-a", 2551);
        private static readonly Address AddressB = new("akka", "sys-b", "host-b", 2552);

        [Fact(DisplayName = "AssociationFor should materialize exactly one Association per address under parallel access")]
        public async Task AssociationFor_should_materialize_a_single_instance_under_parallel_access()
        {
            var registry = new AssociationRegistry();

            var results = await Task.WhenAll(Enumerable.Range(0, 64)
                .Select(_ => Task.Run(() => registry.AssociationFor(AddressA))));

            results.Should().OnlyContain(a => ReferenceEquals(a, results[0]));
        }

        [Fact(DisplayName = "AssociationFor should return distinct Associations for distinct addresses")]
        public void AssociationFor_should_be_distinct_per_address()
        {
            var registry = new AssociationRegistry();

            var a = registry.AssociationFor(AddressA);
            var b = registry.AssociationFor(AddressB);

            a.Should().NotBeSameAs(b);
            a.RemoteAddress.Should().Be(AddressA);
            b.RemoteAddress.Should().Be(AddressB);
        }

        [Fact(DisplayName = "TryGetByUid should be null before handshake and resolve to the association after")]
        public void TryGetByUid_should_be_null_before_and_set_after_handshake()
        {
            var registry = new AssociationRegistry();
            var peer = new UniqueAddress(AddressA, 1L);

            registry.TryGetByUid(peer.Uid).Should().BeNull();

            registry.CompleteHandshake(AddressA, peer);

            registry.TryGetByUid(peer.Uid).Should().BeSameAs(registry.AssociationFor(AddressA));
        }

        [Fact(DisplayName = "A uid change should re-point the reverse index: old uid resolves to nothing, new uid resolves to the (same) association")]
        public void Uid_change_should_repoint_reverse_index()
        {
            var registry = new AssociationRegistry();
            var firstPeer = new UniqueAddress(AddressA, 1L);
            var secondPeer = new UniqueAddress(AddressA, 2L);

            registry.CompleteHandshake(AddressA, firstPeer);
            var association = registry.AssociationFor(AddressA);
            registry.TryGetByUid(firstPeer.Uid).Should().BeSameAs(association);

            registry.CompleteHandshake(AddressA, secondPeer);

            registry.TryGetByUid(firstPeer.Uid).Should().BeNull("the superseded uid must not resolve to anything — it was never quarantined, just abandoned");
            registry.TryGetByUid(secondPeer.Uid).Should().BeSameAs(association, "the ADDRESS-keyed association is reused across incarnations");
        }

        [Fact(DisplayName = "Quarantining the current uid should not remove its reverse-index mapping")]
        public void Quarantine_should_not_remove_reverse_index_mapping()
        {
            var registry = new AssociationRegistry();
            var peer = new UniqueAddress(AddressA, 1L);
            registry.CompleteHandshake(AddressA, peer);
            var association = registry.AssociationFor(AddressA);

            association.Quarantine(peer.Uid).Should().BeTrue();

            registry.TryGetByUid(peer.Uid).Should().BeSameAs(association);
            association.IsQuarantined(peer.Uid).Should().BeTrue();
        }

        [Fact(DisplayName = "CompleteHandshake with the same uid twice should be a no-op that keeps the reverse index intact")]
        public void CompleteHandshake_repeated_with_same_uid_should_be_stable()
        {
            var registry = new AssociationRegistry();
            var peer = new UniqueAddress(AddressA, 1L);

            registry.CompleteHandshake(AddressA, peer);
            registry.CompleteHandshake(AddressA, peer);

            registry.TryGetByUid(peer.Uid).Should().BeSameAs(registry.AssociationFor(AddressA));
        }

        // --- Control channel (task group 6, "Control Stream", task 6.1) ---

        [Fact(DisplayName = "Association should expose a SEPARATE control channel from its ordinary channel, each independently materializable")]
        public void Association_should_expose_independent_ordinary_and_control_channels()
        {
            var registry = new AssociationRegistry();
            var association = registry.AssociationFor(AddressA);

            association.IsOutboundMaterialized.Should().BeFalse();
            association.IsControlOutboundMaterialized.Should().BeFalse();

            var ordinaryMaterialized = 0;
            var controlMaterialized = 0;
            association.EnsureOutboundMaterialized(_ => ordinaryMaterialized++);

            // Materializing the ORDINARY stream must not also materialize (or affect the
            // materialize-gate of) the CONTROL stream -- they are independent latches.
            association.IsOutboundMaterialized.Should().BeTrue();
            association.IsControlOutboundMaterialized.Should().BeFalse();
            ordinaryMaterialized.Should().Be(1);
            controlMaterialized.Should().Be(0);

            association.EnsureControlOutboundMaterialized(_ => controlMaterialized++);

            association.IsControlOutboundMaterialized.Should().BeTrue();
            controlMaterialized.Should().Be(1);

            // Calling either Ensure* again must not re-invoke its materialize callback (each
            // gate is CAS-latched exactly-once, independently of the other).
            association.EnsureOutboundMaterialized(_ => ordinaryMaterialized++);
            association.EnsureControlOutboundMaterialized(_ => controlMaterialized++);
            ordinaryMaterialized.Should().Be(1);
            controlMaterialized.Should().Be(1);
        }

        [Fact(DisplayName = "A full ORDINARY outbound queue must never block enqueuing to the CONTROL queue (queue-level non-starvation proof, task 6.6)")]
        public void Full_ordinary_queue_should_not_block_control_enqueue()
        {
            // Deliberately tiny capacities so the test can force both queues to their bounds
            // quickly and deterministically -- see the type-level remarks on Association's
            // control-channel section for why these are two entirely separate Channel<T>
            // instances, not a priority split of one queue.
            var association = new Association(AddressA, outboundQueueCapacity: 4, controlQueueCapacity: 4);

            // Saturate the ordinary queue completely.
            for (var i = 0; i < 4; i++)
                association.TryEnqueueOutbound(new OutboundEnvelope($"ordinary-{i}", null, null)).Should().BeTrue();

            association.TryEnqueueOutbound(new OutboundEnvelope("ordinary-overflow", null, null))
                .Should().BeFalse("the ordinary queue is now full");

            // The control queue must be COMPLETELY unaffected by the ordinary queue's exhaustion
            // -- this is the structural (not timing-based) half of the non-starvation proof;
            // ArteryControlStreamSpec's end-to-end heartbeat test is the other half.
            for (var i = 0; i < 4; i++)
                association.TryEnqueueControl(new OutboundEnvelope($"control-{i}", null, null)).Should().BeTrue(
                    "the control queue is entirely separate infrastructure from the (now full) ordinary queue");

            association.TryEnqueueControl(new OutboundEnvelope("control-overflow", null, null))
                .Should().BeFalse("the control queue has its OWN bound, reached independently of the ordinary queue's state");
        }

        [Fact(DisplayName = "ShouldLogQuarantineDrop should return true exactly once per uid (task 6.6: log once per association/uid, not per message)")]
        public void ShouldLogQuarantineDrop_should_latch_once_per_uid()
        {
            var association = new Association(AddressA);

            association.ShouldLogQuarantineDrop(1L).Should().BeTrue("the first drop for uid 1 should be logged");
            association.ShouldLogQuarantineDrop(1L).Should().BeFalse("subsequent drops for the SAME uid must not be logged again");
            association.ShouldLogQuarantineDrop(1L).Should().BeFalse();

            association.ShouldLogQuarantineDrop(2L).Should().BeTrue("a DIFFERENT uid (e.g. a new incarnation) gets its own fresh unlogged state");
        }
    }
}
