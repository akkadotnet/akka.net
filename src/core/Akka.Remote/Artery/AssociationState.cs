//-----------------------------------------------------------------------
// <copyright file="AssociationState.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System.Collections.Immutable;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Immutable snapshot of one association's UID-scoped lifecycle state:
    /// <c>Associating</c> (UID unknown, <see cref="UniqueRemoteAddress"/> is <c>null</c>) →
    /// <c>Associated</c> (<see cref="UniqueRemoteAddress"/> set by <see cref="CompleteHandshake"/>) →
    /// a different incoming UID (remote restart) swaps in a new incarnation.
    ///
    /// <para>
    /// This type holds no mutable state and does no I/O — it is a pure value plus the two
    /// transition functions (<see cref="CompleteHandshake"/>, <see cref="Quarantine"/>) that
    /// compute the next snapshot. The owner (<see cref="Association"/>) holds a <c>volatile</c>
    /// reference to the current snapshot and swaps it with
    /// <see cref="System.Threading.Interlocked.CompareExchange{T}(ref T, T, T)"/> in a CAS retry
    /// loop — see <see cref="Association.CompleteHandshake"/> / <see cref="Association.Quarantine"/>.
    /// </para>
    /// <para>
    /// Faithful to <c>openspec/changes/artery-tcp-remoting/design.md</c>
    /// ("Handshake + association/UID (gate G2)", "Association state machine" + "Quarantine
    /// (UID-scoped)"): a uid change is <b>not</b> an auto-quarantine of the old uid — only an
    /// explicit <see cref="Quarantine"/> call does that, and only for the <em>current</em> uid
    /// (a stale-uid quarantine request is ignored).
    /// </para>
    /// </summary>
    internal sealed class AssociationState
    {
        private AssociationState(int incarnation, UniqueAddress? uniqueRemoteAddress, ImmutableHashSet<long> quarantinedUids)
        {
            Incarnation = incarnation;
            UniqueRemoteAddress = uniqueRemoteAddress;
            QuarantinedUids = quarantinedUids;
        }

        /// <summary>
        /// The initial state for a freshly-materialized association: no peer UID known yet
        /// (<c>Associating</c>), incarnation 1, no quarantined UIDs.
        /// </summary>
        public static AssociationState Create() => new(incarnation: 1, uniqueRemoteAddress: null, quarantinedUids: ImmutableHashSet<long>.Empty);

        /// <summary>
        /// Monotonically increasing incarnation counter. Starts at 1; incremented only when
        /// <see cref="CompleteHandshake"/> observes a peer UID different from the current one
        /// (the remote system restarted under the same address).
        /// </summary>
        public int Incarnation { get; }

        /// <summary>
        /// The peer's address + UID once the handshake has completed at least once for this
        /// incarnation; <c>null</c> while <c>Associating</c> (UID unknown).
        /// </summary>
        public UniqueAddress? UniqueRemoteAddress { get; }

        /// <summary>
        /// The set of peer UIDs (for this association's remote address) that have been
        /// explicitly quarantined. A uid change alone does not add the superseded uid here —
        /// see the type-level remarks.
        /// </summary>
        public ImmutableHashSet<long> QuarantinedUids { get; }

        /// <summary>
        /// Whether <paramref name="uid"/> has been quarantined.
        /// </summary>
        public bool IsQuarantined(long uid) => QuarantinedUids.Contains(uid);

        /// <summary>
        /// Computes the state that results from completing (or re-completing) the handshake
        /// with <paramref name="peer"/>:
        /// <list type="bullet">
        /// <item><description><c>Associating</c> → <c>Associated</c>: adopts <paramref name="peer"/>, incarnation unchanged.</description></item>
        /// <item><description>Same uid as the current <see cref="UniqueRemoteAddress"/>: no-op — returns <c>this</c> (reference-equal, so the CAS loop in <see cref="Association"/> can skip the compare-exchange).</description></item>
        /// <item><description>Different uid (remote restart): a new incarnation — <see cref="Incarnation"/> + 1, <see cref="UniqueRemoteAddress"/> replaced, <see cref="QuarantinedUids"/> carried over UNCHANGED (the old uid is deliberately not auto-quarantined).</description></item>
        /// </list>
        /// </summary>
        public AssociationState CompleteHandshake(UniqueAddress peer)
        {
            if (UniqueRemoteAddress is { } current)
            {
                if (current.Uid == peer.Uid)
                    return this;

                return new AssociationState(Incarnation + 1, peer, QuarantinedUids);
            }

            return new AssociationState(Incarnation, peer, QuarantinedUids);
        }

        /// <summary>
        /// Computes the state that results from quarantining <paramref name="uid"/>. Acts ONLY
        /// if <paramref name="uid"/> equals the current <see cref="UniqueRemoteAddress"/>'s uid —
        /// a stale-uid request (from a superseded incarnation) is ignored. The caller
        /// (<see cref="Association.Quarantine"/>) reports whether the uid was current via the
        /// returned <c>Acted</c> flag; <c>NewState</c> is reference-equal to <c>this</c> when
        /// nothing changed (already quarantined, or stale uid).
        /// </summary>
        public (AssociationState NewState, bool Acted) Quarantine(long uid)
        {
            if (UniqueRemoteAddress is not { } current || current.Uid != uid)
                return (this, false);

            if (QuarantinedUids.Contains(uid))
                return (this, true);

            return (new AssociationState(Incarnation, UniqueRemoteAddress, QuarantinedUids.Add(uid)), true);
        }
    }
}
