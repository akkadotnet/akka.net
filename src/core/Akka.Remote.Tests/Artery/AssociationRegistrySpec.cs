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
    }
}
