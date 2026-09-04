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
    /// <para>
    /// <b>Compare snapshots by REFERENCE, never with <c>==</c>/<see cref="object.Equals(object)"/>.</b>
    /// Being a record, this type has compiler-generated value equality — which is shallow over
    /// <see cref="QuarantinedUids"/> (<see cref="ImmutableHashSet{T}"/> does not define structural
    /// equality) and therefore means nothing useful here. Every caller that matters is asking
    /// "is this the SAME snapshot I already had?", which is exactly what the transition methods
    /// promise by returning <c>this</c> unchanged on a no-op, and what the CAS loops in
    /// <see cref="Association"/> rely on.
    /// </para>
    /// </summary>
    internal sealed record AssociationState
    {
        private AssociationState(
            int incarnation,
            UniqueAddress? uniqueRemoteAddress,
            bool outboundHandshakeCompleted,
            ImmutableHashSet<long> quarantinedUids)
        {
            Incarnation = incarnation;
            UniqueRemoteAddress = uniqueRemoteAddress;
            OutboundHandshakeCompleted = outboundHandshakeCompleted;
            QuarantinedUids = quarantinedUids;
        }

        /// <summary>
        /// The initial state for a freshly-materialized association: no peer UID known yet
        /// (<c>Associating</c>), incarnation 1, our own handshake unanswered, no quarantined UIDs.
        /// </summary>
        public static AssociationState Create() =>
            new(incarnation: 1, uniqueRemoteAddress: null, outboundHandshakeCompleted: false, quarantinedUids: ImmutableHashSet<long>.Empty);

        /// <summary>
        /// Monotonically increasing incarnation counter. Starts at 1; incremented only when
        /// <see cref="CompleteHandshake"/> observes a peer UID different from the current one
        /// (the remote system restarted under the same address).
        /// </summary>
        public int Incarnation { get; init; }

        /// <summary>
        /// The peer's address + UID once the handshake has completed at least once for this
        /// incarnation; <c>null</c> while <c>Associating</c> (UID unknown).
        /// </summary>
        public UniqueAddress? UniqueRemoteAddress { get; init; }

        /// <summary>
        /// Whether THIS side's own <see cref="HandshakeReq"/> has been answered for the CURRENT
        /// incarnation. Set ONLY by <see cref="CompleteOutboundHandshake"/>; a uid change resets it
        /// and <see cref="Quarantine"/> clears it, so it never outlives the incarnation it
        /// describes. <see cref="UniqueRemoteAddress"/> cannot stand in for it: the inbound
        /// direction (the peer's own Req) sets that field too, and knowing the peer's uid says
        /// nothing about whether the peer knows OURS (issue #8496).
        /// </summary>
        public bool OutboundHandshakeCompleted { get; init; }

        /// <summary>
        /// The set of peer UIDs (for this association's remote address) that have been
        /// explicitly quarantined. A uid change alone does not add the superseded uid here —
        /// see the type-level remarks.
        /// </summary>
        public ImmutableHashSet<long> QuarantinedUids { get; init; }

        /// <summary>
        /// Whether <paramref name="uid"/> has been quarantined.
        /// </summary>
        public bool IsQuarantined(long uid) => QuarantinedUids.Contains(uid);

        /// <summary>
        /// Computes the state that results from completing (or re-completing) the handshake
        /// with <paramref name="peer"/>:
        /// <list type="bullet">
        /// <item><description><c>Associating</c> → <c>Associated</c>: adopts <paramref name="peer"/>, incarnation unchanged.</description></item>
        /// <item><description>Same uid as the current <see cref="UniqueRemoteAddress"/>: no-op — returns <c>this</c> (reference-equal, so the CAS loop in <see cref="Association"/> can skip the compare-exchange), except for the one-way <see cref="OutboundHandshakeCompleted"/> flip in <see cref="CompleteOutboundHandshake"/>.</description></item>
        /// <item><description>Different uid (remote restart): a new incarnation — <see cref="Incarnation"/> + 1, <see cref="UniqueRemoteAddress"/> replaced, <see cref="QuarantinedUids"/> carried over UNCHANGED (the old uid is deliberately not auto-quarantined).</description></item>
        /// </list>
        /// </summary>
        public AssociationState CompleteHandshake(UniqueAddress peer) => Apply(peer, answeredOurReq: false);

        /// <summary>
        /// As <see cref="CompleteHandshake"/>, but for the ONE event that proves the peer has
        /// registered our uid: a <see cref="HandshakeRsp"/>, which a peer only sends after handling
        /// a <see cref="HandshakeReq"/> of ours. Sets <see cref="OutboundHandshakeCompleted"/> --
        /// nothing else does. A same-uid call that only flips that flag still returns a NEW
        /// snapshot (it is a real transition, not the documented no-op).
        /// </summary>
        public AssociationState CompleteOutboundHandshake(UniqueAddress peer) => Apply(peer, answeredOurReq: true);

        private AssociationState Apply(UniqueAddress peer, bool answeredOurReq)
        {
            if (UniqueRemoteAddress is { } current)
            {
                if (current.Uid == peer.Uid)
                {
                    // Returning THIS (not a `with` copy) is load-bearing: the CAS loops in
                    // Association detect "nothing to publish" by reference.
                    if (!answeredOurReq || OutboundHandshakeCompleted)
                        return this;

                    return this with { OutboundHandshakeCompleted = true };
                }

                // A different uid is a new incarnation; QuarantinedUids carries over untouched,
                // which is what `with` does for every field this transition does not name.
                return this with
                {
                    Incarnation = Incarnation + 1,
                    UniqueRemoteAddress = peer,
                    OutboundHandshakeCompleted = answeredOurReq
                };
            }

            return this with
            {
                UniqueRemoteAddress = peer,
                OutboundHandshakeCompleted = answeredOurReq
            };
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

            // OutboundHandshakeCompleted is cleared with the incarnation it describes: this uid is
            // cut off, so no later stream may trust "the peer knows us" on its behalf.
            return (this with
            {
                OutboundHandshakeCompleted = false,
                QuarantinedUids = QuarantinedUids.Add(uid)
            }, true);
        }
    }
}
