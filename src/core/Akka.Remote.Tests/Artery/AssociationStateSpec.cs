//-----------------------------------------------------------------------
// <copyright file="AssociationStateSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.Artery;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Unit tests for the G2 handshake/association-UID lock-free state machine:
    /// <see cref="AssociationState"/> (the pure immutable snapshot + transition functions) and
    /// <see cref="Association"/> (the CAS retry loop owner). No <c>ActorSystem</c> is needed —
    /// pure state, same style as the sibling G1 <c>ArteryFramingSpec</c>. See
    /// <c>openspec/changes/artery-tcp-remoting/design.md</c>
    /// ("Handshake + association/UID (gate G2)", "Association state machine" + "Quarantine
    /// (UID-scoped)").
    /// </summary>
    public class AssociationStateSpec
    {
        private static readonly Address RemoteAddress = new("akka", "remote-sys", "remote-host", 2552);

        #region AssociationState (pure transitions)

        [Fact(DisplayName = "AssociationState.Create should start Associating with incarnation 1")]
        public void Create_should_start_Associating_with_incarnation_1()
        {
            var state = AssociationState.Create();

            state.Incarnation.Should().Be(1);
            state.UniqueRemoteAddress.Should().BeNull();
            state.QuarantinedUids.Should().BeEmpty();
        }

        [Fact(DisplayName = "CompleteHandshake should move Associating to Associated without bumping incarnation")]
        public void CompleteHandshake_should_move_Associating_to_Associated()
        {
            var initial = AssociationState.Create();
            var peer = new UniqueAddress(RemoteAddress, 1L);

            var updated = initial.CompleteHandshake(peer);

            updated.Incarnation.Should().Be(1);
            updated.UniqueRemoteAddress.Should().Be(peer);
        }

        [Fact(DisplayName = "CompleteHandshake with the same uid should be an idempotent no-op")]
        public void CompleteHandshake_with_same_uid_should_be_idempotent()
        {
            var peer = new UniqueAddress(RemoteAddress, 1L);
            var associated = AssociationState.Create().CompleteHandshake(peer);

            var again = associated.CompleteHandshake(peer);

            again.Should().BeSameAs(associated);
        }

        [Fact(DisplayName = "CompleteHandshake with a different uid should bump the incarnation and NOT auto-quarantine the old uid")]
        public void CompleteHandshake_with_different_uid_should_bump_incarnation_without_auto_quarantine()
        {
            var firstPeer = new UniqueAddress(RemoteAddress, 1L);
            var secondPeer = new UniqueAddress(RemoteAddress, 2L);
            var associated = AssociationState.Create().CompleteHandshake(firstPeer);

            var restarted = associated.CompleteHandshake(secondPeer);

            restarted.Incarnation.Should().Be(associated.Incarnation + 1);
            restarted.UniqueRemoteAddress.Should().Be(secondPeer);
            restarted.IsQuarantined(firstPeer.Uid).Should().BeFalse("a uid change alone must not auto-quarantine the superseded uid");
            restarted.QuarantinedUids.Should().BeEmpty();
        }

        [Fact(DisplayName = "Quarantine should ignore a stale (superseded) uid and return Acted=false")]
        public void Quarantine_should_ignore_stale_uid()
        {
            var firstPeer = new UniqueAddress(RemoteAddress, 1L);
            var secondPeer = new UniqueAddress(RemoteAddress, 2L);
            var restarted = AssociationState.Create().CompleteHandshake(firstPeer).CompleteHandshake(secondPeer);

            var (newState, acted) = restarted.Quarantine(firstPeer.Uid);

            acted.Should().BeFalse();
            newState.Should().BeSameAs(restarted);
            newState.IsQuarantined(firstPeer.Uid).Should().BeFalse();
        }

        [Fact(DisplayName = "Quarantine should act on the current uid and mark it quarantined")]
        public void Quarantine_should_act_on_current_uid()
        {
            var peer = new UniqueAddress(RemoteAddress, 1L);
            var associated = AssociationState.Create().CompleteHandshake(peer);

            var (newState, acted) = associated.Quarantine(peer.Uid);

            acted.Should().BeTrue();
            newState.Should().NotBeSameAs(associated);
            newState.IsQuarantined(peer.Uid).Should().BeTrue();
        }

        [Fact(DisplayName = "Quarantine should be idempotent for an already-quarantined uid")]
        public void Quarantine_should_be_idempotent()
        {
            var peer = new UniqueAddress(RemoteAddress, 1L);
            var associated = AssociationState.Create().CompleteHandshake(peer);
            var (quarantined, _) = associated.Quarantine(peer.Uid);

            var (again, acted) = quarantined.Quarantine(peer.Uid);

            acted.Should().BeTrue();
            again.Should().BeSameAs(quarantined);
        }

        [Fact(DisplayName = "IsQuarantined should be false for a uid that was never associated")]
        public void IsQuarantined_should_be_false_for_unknown_uid()
        {
            AssociationState.Create().IsQuarantined(999L).Should().BeFalse();
        }

        [Fact(DisplayName = "Quarantine before any handshake (Associating) should be ignored (no current uid to match)")]
        public void Quarantine_before_handshake_should_be_ignored()
        {
            var initial = AssociationState.Create();

            var (newState, acted) = initial.Quarantine(1L);

            acted.Should().BeFalse();
            newState.Should().BeSameAs(initial);
        }

        #endregion

        #region Association (CAS retry loop owner)

        [Fact(DisplayName = "Association.CompleteHandshake should report Previous/Updated across an incarnation change")]
        public void Association_CompleteHandshake_should_report_previous_and_updated()
        {
            var association = new Association(RemoteAddress);
            var firstPeer = new UniqueAddress(RemoteAddress, 1L);
            var secondPeer = new UniqueAddress(RemoteAddress, 2L);

            association.CompleteHandshake(firstPeer);
            var (previous, updated) = association.CompleteHandshake(secondPeer);

            previous.UniqueRemoteAddress.Should().Be(firstPeer);
            updated.UniqueRemoteAddress.Should().Be(secondPeer);
            updated.Incarnation.Should().Be(previous.Incarnation + 1);
            association.CurrentState.Should().BeSameAs(updated);
        }

        [Fact(DisplayName = "Association.Quarantine should return false for a stale uid and true for the current uid")]
        public void Association_Quarantine_should_gate_on_current_uid()
        {
            var association = new Association(RemoteAddress);
            var firstPeer = new UniqueAddress(RemoteAddress, 1L);
            var secondPeer = new UniqueAddress(RemoteAddress, 2L);
            association.CompleteHandshake(firstPeer);
            association.CompleteHandshake(secondPeer);

            association.Quarantine(firstPeer.Uid).Should().BeFalse();
            association.Quarantine(secondPeer.Uid).Should().BeTrue();
            association.IsQuarantined(secondPeer.Uid).Should().BeTrue();
        }

        [Fact(DisplayName = "Association.CompleteHandshake should not lose updates under concurrent uid-changing races (CAS stress)")]
        public async Task Association_CompleteHandshake_should_not_lose_updates_under_concurrent_races()
        {
            const int taskCount = 64;
            var association = new Association(RemoteAddress);

            var tasks = Enumerable.Range(1, taskCount)
                .Select(uid => Task.Run(() => association.CompleteHandshake(new UniqueAddress(RemoteAddress, uid))))
                .ToArray();

            await Task.WhenAll(tasks);

            // First transition (Associating -> Associated) does not bump the incarnation; each of
            // the remaining (taskCount - 1) distinct-uid transitions does, since every task's uid
            // is pairwise distinct -> every applied CAS is necessarily a real uid change once the
            // association is already Associated.
            association.CurrentState.Incarnation.Should().Be(taskCount);
            association.CurrentState.UniqueRemoteAddress.Should().NotBeNull();
            association.CurrentState.QuarantinedUids.Should().BeEmpty("a uid change alone must never auto-quarantine");
        }

        [Fact(DisplayName = "Association.Quarantine should not lose updates when raced concurrently for the current uid (CAS stress)")]
        public async Task Association_Quarantine_should_not_lose_updates_under_concurrent_races()
        {
            const int taskCount = 32;
            var association = new Association(RemoteAddress);
            var peer = new UniqueAddress(RemoteAddress, 42L);
            association.CompleteHandshake(peer);

            var results = await Task.WhenAll(Enumerable.Range(0, taskCount)
                .Select(_ => Task.Run(() => association.Quarantine(peer.Uid))));

            results.Should().OnlyContain(acted => acted);
            association.IsQuarantined(peer.Uid).Should().BeTrue();
        }

        [Fact(DisplayName = "Association state should remain internally consistent under mixed concurrent CompleteHandshake + Quarantine races")]
        public async Task Association_should_remain_consistent_under_mixed_concurrent_races()
        {
            var association = new Association(RemoteAddress);
            var initialPeer = new UniqueAddress(RemoteAddress, 1L);
            association.CompleteHandshake(initialPeer);

            var tasks = new List<Task>();
            for (var i = 0; i < 50; i++)
                tasks.Add(Task.Run(() => association.Quarantine(initialPeer.Uid)));

            for (var uid = 2; uid <= 20; uid++)
            {
                var capturedUid = uid;
                tasks.Add(Task.Run(() => association.CompleteHandshake(new UniqueAddress(RemoteAddress, capturedUid))));
            }

            await Task.WhenAll(tasks);

            // No exceptions/hangs (the CAS loops always make progress), and the resulting
            // incarnation is bounded by the number of possible distinct-uid transitions.
            association.CurrentState.Incarnation.Should().BeInRange(1, 20);
            association.CurrentState.UniqueRemoteAddress.Should().NotBeNull();
        }

        #endregion
    }
}
