//-----------------------------------------------------------------------
// <copyright file="AssociationRegistry.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using Akka.Actor;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Owns the lock-free <see cref="AssociationState"/> snapshot for one remote
    /// <see cref="Actor.Address"/> and provides the CAS retry loops that transition it.
    /// Transport/queue wiring (the outbound send queue, lanes, etc.) is added by a later chunk —
    /// at G2 this class holds only association state.
    /// </summary>
    internal sealed class Association
    {
        private volatile AssociationState _state;

        public Association(Address remoteAddress)
        {
            RemoteAddress = remoteAddress;
            _state = AssociationState.Create();
        }

        /// <summary>
        /// The remote address this association is keyed by.
        /// </summary>
        public Address RemoteAddress { get; }

        /// <summary>
        /// The current immutable state snapshot. Safe to read from any thread.
        /// </summary>
        public AssociationState CurrentState => _state;

        /// <summary>
        /// CAS loop applying <see cref="AssociationState.CompleteHandshake"/>. Returns both the
        /// snapshot immediately before this call's effective transition and the resulting
        /// snapshot, so <see cref="AssociationRegistry"/> can tell — without a separate,
        /// racy read — whether (and from what uid) an incarnation change just happened.
        /// </summary>
        public (AssociationState Previous, AssociationState Updated) CompleteHandshake(UniqueAddress peer)
        {
            while (true)
            {
                var current = _state;
                var updated = current.CompleteHandshake(peer);

                if (ReferenceEquals(updated, current))
                    return (current, current);

                if (Interlocked.CompareExchange(ref _state, updated, current) == current)
                    return (current, updated);
            }
        }

        /// <summary>
        /// CAS loop applying <see cref="AssociationState.Quarantine"/>. Returns <c>false</c> when
        /// <paramref name="uid"/> is not the current uid (stale-uid request, ignored).
        /// </summary>
        public bool Quarantine(long uid)
        {
            while (true)
            {
                var current = _state;
                var (updated, acted) = current.Quarantine(uid);

                if (!acted)
                    return false;

                if (ReferenceEquals(updated, current))
                    return true;

                if (Interlocked.CompareExchange(ref _state, updated, current) == current)
                    return true;
            }
        }

        /// <summary>
        /// Whether <paramref name="uid"/> is currently quarantined for this association.
        /// </summary>
        public bool IsQuarantined(long uid) => _state.IsQuarantined(uid);
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// Address-keyed, CAS-materialized association registry plus a uid → association reverse
    /// index populated on handshake completion.
    ///
    /// <para>
    /// <b>Reverse-index policy (uid change).</b> When <see cref="CompleteHandshake"/> observes a
    /// peer uid different from the association's current one (a remote restart under the same
    /// address — a new incarnation), the OLD uid's reverse-index entry is removed: it does not
    /// carry over to the quarantined association, because a plain uid change does not
    /// auto-quarantine the old uid (see <see cref="AssociationState"/> remarks) — there is
    /// nothing meaningful for the old uid to resolve to anymore, so
    /// <see cref="TryGetByUid"/> returns <c>null</c> for it. The removal uses
    /// <see cref="ICollection{T}.Remove"/> on the exact <c>(uid, association)</c> pair (a
    /// conditional / compare-value removal on <see cref="ConcurrentDictionary{TKey,TValue}"/>),
    /// so a racing, newer update can never be clobbered by a stale one. If an association is
    /// later explicitly quarantined via <see cref="Association.Quarantine"/> for its CURRENT uid,
    /// the reverse-index entry for that (current) uid is untouched — it keeps resolving to the
    /// (now quarantined) association, matching design.md's "the old UID stays quarantined" for
    /// that scenario.
    /// </para>
    /// </summary>
    internal sealed class AssociationRegistry
    {
        private readonly ConcurrentDictionary<Address, Association> _byAddress = new();
        private readonly ConcurrentDictionary<long, Association> _byUid = new();

        /// <summary>
        /// Returns the <see cref="Association"/> for <paramref name="remoteAddress"/>, creating
        /// it (via <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey, System.Func{TKey,TValue})"/>)
        /// if this is the first reference to that address.
        /// </summary>
        public Association AssociationFor(Address remoteAddress) => _byAddress.GetOrAdd(remoteAddress, static addr => new Association(addr));

        /// <summary>
        /// Looks up the association currently known to own <paramref name="uid"/>. Returns
        /// <c>null</c> before any handshake has completed for that uid, or after a uid change has
        /// superseded it (see the reverse-index policy in the type remarks).
        /// </summary>
        public Association? TryGetByUid(long uid) => _byUid.TryGetValue(uid, out var association) ? association : null;

        /// <summary>
        /// Completes the handshake for <paramref name="remoteAddress"/> with peer
        /// <paramref name="peer"/>: materializes the address-keyed association if needed, applies
        /// the CAS transition, and maintains the uid reverse index per the policy documented on
        /// this type.
        /// </summary>
        public AssociationState CompleteHandshake(Address remoteAddress, UniqueAddress peer)
        {
            var association = AssociationFor(remoteAddress);
            var (previous, updated) = association.CompleteHandshake(peer);

            if (!ReferenceEquals(previous, updated) &&
                previous.UniqueRemoteAddress is { } previousPeer &&
                previousPeer.Uid != peer.Uid)
            {
                // Conditional remove: only removes if the old uid still points at THIS
                // association (a concurrent, newer mapping is never clobbered).
                ((ICollection<KeyValuePair<long, Association>>)_byUid).Remove(
                    new KeyValuePair<long, Association>(previousPeer.Uid, association));
            }

            _byUid[peer.Uid] = association;
            return updated;
        }
    }
}
