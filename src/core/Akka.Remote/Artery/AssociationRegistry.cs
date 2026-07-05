//-----------------------------------------------------------------------
// <copyright file="AssociationRegistry.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using Akka.Actor;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Owns the lock-free <see cref="AssociationState"/> snapshot for one remote
    /// <see cref="Actor.Address"/>, the CAS retry loops that transition it, AND (G2 transport chunk)
    /// the association's bounded outbound queue + once-only outbound-stream materialization
    /// lifecycle (design.md Decision 7/9: a bounded <c>Channel</c>, externally owned so it survives
    /// stream restart -- reconnect re-attaches a new consumer to the SAME channel).
    /// </summary>
    internal sealed class Association
    {
        /// <summary>
        /// Default capacity for <see cref="OutboundReader"/>'s bounded channel (Decision 7/9). A
        /// sane constant rather than a new <c>ArterySettings</c>/<c>Remote.conf</c> key -- see the
        /// G2 transport-chunk task report for why.
        /// </summary>
        public const int DefaultOutboundQueueCapacity = 3072;

        private volatile AssociationState _state;
        private readonly Channel<ArteryOutboundElement> _outboundChannel;
        private int _outboundMaterializeStarted;

        public Association(Address remoteAddress, int outboundQueueCapacity = DefaultOutboundQueueCapacity)
        {
            RemoteAddress = remoteAddress;
            _state = AssociationState.Create();
            _outboundChannel = Channel.CreateBounded<ArteryOutboundElement>(new BoundedChannelOptions(outboundQueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
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
        /// The reading side of this association's bounded outbound queue. Consumed by exactly one
        /// materialized outbound stream (<see cref="Akka.Streams.Dsl.ChannelSource.FromReader{T}"/>),
        /// per <see cref="EnsureOutboundMaterialized"/>.
        /// </summary>
        public ChannelReader<ArteryOutboundElement> OutboundReader => _outboundChannel.Reader;

        /// <summary>
        /// Whether <see cref="EnsureOutboundMaterialized"/> has already started (or finished)
        /// materializing this association's outbound stream. A cheap check callers can use to skip
        /// allocating a materialize callback on the (post-first-call) steady-state path.
        /// </summary>
        public bool IsOutboundMaterialized => Volatile.Read(ref _outboundMaterializeStarted) != 0;

        /// <summary>
        /// Attempts to enqueue <paramref name="element"/> for the outbound stream to send.
        /// Non-blocking (<see cref="ChannelWriter{T}.TryWrite"/>) -- NEVER awaits/blocks a producing
        /// actor thread on a slow remote (Decision 7). Returns <see langword="false"/> when the
        /// bounded queue is full; the caller (<c>ArteryRemoting</c>) applies the overflow policy
        /// (ordinary messages -> dead letters).
        /// </summary>
        public bool TryEnqueueOutbound(ArteryOutboundElement element) => _outboundChannel.Writer.TryWrite(element);

        /// <summary>
        /// Ensures this association's outbound stream is materialized exactly once, no matter how
        /// many threads call this concurrently -- only the FIRST caller's <paramref name="materialize"/>
        /// callback executes (CAS-gated on an internal flag). The callback is supplied by the
        /// transport (<c>ArteryRemoting</c>), which owns the Tcp extension / materializer / settings
        /// this pure state type deliberately does not know about.
        /// </summary>
        public void EnsureOutboundMaterialized(Action<Association> materialize)
        {
            if (Interlocked.CompareExchange(ref _outboundMaterializeStarted, 1, 0) == 0)
                materialize(this);
        }

        /// <summary>
        /// Marks the outbound channel complete (no further writes accepted) so its materialized
        /// <see cref="Akka.Streams.Dsl.ChannelSource.FromReader{T}"/> consumer finishes gracefully.
        /// Called on transport shutdown.
        /// </summary>
        public void CompleteOutbound() => _outboundChannel.Writer.TryComplete();

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
        private readonly int _outboundQueueCapacity;

        /// <summary>
        /// Initializes a new instance of the <see cref="AssociationRegistry"/> class.
        /// </summary>
        /// <param name="outboundQueueCapacity">
        /// Capacity of every materialized <see cref="Association"/>'s bounded outbound channel
        /// (see <see cref="Association.DefaultOutboundQueueCapacity"/>).
        /// </param>
        public AssociationRegistry(int outboundQueueCapacity = Association.DefaultOutboundQueueCapacity)
        {
            _outboundQueueCapacity = outboundQueueCapacity;
        }

        /// <summary>
        /// Returns the <see cref="Association"/> for <paramref name="remoteAddress"/>, creating
        /// it (via <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey, System.Func{TKey,TValue})"/>)
        /// if this is the first reference to that address.
        /// </summary>
        public Association AssociationFor(Address remoteAddress) =>
            _byAddress.GetOrAdd(remoteAddress, (addr, capacity) => new Association(addr, capacity), _outboundQueueCapacity);

        /// <summary>
        /// Looks up the association currently known to own <paramref name="uid"/>. Returns
        /// <c>null</c> before any handshake has completed for that uid, or after a uid change has
        /// superseded it (see the reverse-index policy in the type remarks).
        /// </summary>
        public Association? TryGetByUid(long uid) => _byUid.TryGetValue(uid, out var association) ? association : null;

        /// <summary>
        /// A point-in-time snapshot of every association currently known to this registry. Used by
        /// <c>ArteryRemoting.Shutdown</c> to complete every association's outbound channel.
        /// </summary>
        public ICollection<Association> AllAssociations => _byAddress.Values;

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
