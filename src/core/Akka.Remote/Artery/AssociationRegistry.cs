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
    /// CAS-gated "materialize exactly once" latch, shared by the ordinary and control outbound
    /// stream materialization paths on <see cref="Association"/> (design.md task group 6, task
    /// 6.1: "factor the shared shape into a helper rather than duplicating"). Each
    /// <see cref="Association"/> owns TWO independent instances of this type -- one per outbound
    /// stream -- so materializing one never affects the other's gate.
    /// </summary>
    internal sealed class MaterializeOnceGate
    {
        private int _started;

        /// <summary>
        /// Whether <see cref="EnsureStarted"/> has already started (or finished) materializing.
        /// A cheap check callers can use to skip allocating a materialize callback on the
        /// (post-first-call) steady-state path.
        /// </summary>
        public bool IsStarted => Volatile.Read(ref _started) != 0;

        /// <summary>
        /// Runs <paramref name="materialize"/> exactly once, no matter how many threads call this
        /// concurrently -- only the FIRST caller's callback executes (CAS-gated on an internal flag).
        /// </summary>
        public void EnsureStarted(Action materialize)
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) == 0)
                materialize();
        }
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// Owns the lock-free <see cref="AssociationState"/> snapshot for one remote
    /// <see cref="Actor.Address"/>, the CAS retry loops that transition it, AND (G2 transport chunk;
    /// extended at task group 6, "Control Stream") the association's TWO bounded outbound queues
    /// -- ordinary and control -- plus their once-only outbound-stream materialization lifecycles
    /// (design.md Decision 7/9: bounded <c>Channel</c>s, externally owned so they survive stream
    /// restart -- reconnect re-attaches a new consumer to the SAME channel).
    ///
    /// <para>
    /// <b>Two independent channels, one per stream (task 6.1).</b> The control channel is
    /// deliberately separate infrastructure from the ordinary channel -- not a priority lane
    /// carved out of the same queue -- so that the ordinary queue filling up can NEVER block or
    /// starve control traffic (design.md "Control stream before lanes" / Decision 5, and the
    /// Invariants section's "quarantine gating at the send-routing layer" / "control stream stays
    /// alive and drainable while quarantined"). Its capacity is intentionally smaller than the
    /// ordinary queue's -- control traffic (handshake, heartbeat, quarantine notice, and reliable
    /// system-message envelopes) is low-volume relative to ordinary user traffic.
    /// <b>GROUP7 RESOLVED (design.md gate G3):</b> design.md Decision 7's control/system overflow
    /// -&gt; QUARANTINE policy is implemented in <c>ArteryRemoting.HandleControlOverflow</c> (called
    /// from both <c>EnqueueControl</c> and <c>EnqueueSystemMessage</c> on a full channel) -- this is
    /// a SEPARATE, much-smaller-capacity overflow point than <c>SystemMessageDeliveryStage</c>'s own
    /// internal unacknowledged-buffer overflow (<c>system-message-buffer-size</c>, default 20000):
    /// the channel here is producer-side backpressure (protects a producing actor thread from a slow
    /// socket), while the stage's buffer is the RELIABILITY window (how long a sent-but-unacked
    /// message may wait before giving up) -- both funnel into the same quarantine outcome.
    /// </para>
    /// </summary>
    internal sealed class Association
    {
        /// <summary>
        /// Default capacity for <see cref="OutboundReader"/>'s bounded channel (Decision 7/9). A
        /// sane constant rather than a new <c>ArterySettings</c>/<c>Remote.conf</c> key -- see the
        /// G2 transport-chunk task report for why.
        /// </summary>
        public const int DefaultOutboundQueueCapacity = 3072;

        /// <summary>
        /// Default capacity for <see cref="ControlReader"/>'s bounded channel. Deliberately smaller
        /// than <see cref="DefaultOutboundQueueCapacity"/> -- control traffic (handshake, heartbeat,
        /// quarantine notice, reliable system-message envelopes and their Ack/Nack replies) is
        /// low-volume relative to ordinary user traffic (design.md task group 6, task 6.1). The full
        /// asymmetric overflow policy (control overflow -> quarantine, per Decision 7) is implemented
        /// -- see the type-level "GROUP7 RESOLVED" remarks.
        /// </summary>
        public const int DefaultControlQueueCapacity = 256;

        private volatile AssociationState _state;

        // Typed as the IOutboundEnvelope INTERFACE (not the concrete OutboundEnvelope record) so
        // ChannelSource.FromReader(OutboundReader) yields a Source<IOutboundEnvelope, _> that feeds
        // OutboundHandshakeStage's Inlet<IOutboundEnvelope> directly -- no upcast .Select needed
        // between the channel and the handshake stage (design.md Decision 9's ChannelSource.FromReader
        // mandate; see the G3 opening-refactor task report for why the interface, not the concrete
        // type, is the channel's type parameter).
        private readonly Channel<IOutboundEnvelope> _outboundChannel;
        private readonly Channel<IOutboundEnvelope> _controlChannel;
        private readonly MaterializeOnceGate _outboundGate = new();
        private readonly MaterializeOnceGate _controlGate = new();

        /// <summary>
        /// Per-uid "have we already logged a quarantine-drop for this uid" latch (task 6.6:
        /// "log once per association, not per message"). Keyed by uid (not just a single
        /// per-association flag) so a NEW incarnation -- a different uid, possibly quarantined
        /// again in the future -- gets its own fresh unlogged state.
        /// </summary>
        private readonly ConcurrentDictionary<long, bool> _quarantineDropLogged = new();

        /// <summary>
        /// Same latch shape as <see cref="_quarantineDropLogged"/>, applied to the ORDINARY
        /// outbound queue's overflow-drop warning (a flooded producer can otherwise log once PER
        /// DROPPED MESSAGE -- thousands of formatted log lines during a burst -- which was itself
        /// an amplifier of unrelated ThreadPool starvation observed under CI load). Keyed by uid
        /// for the same reason: a reconnect (new incarnation) gets its own fresh unlogged state,
        /// rather than a stale latch from a prior incarnation permanently silencing the warning.
        /// Sends observed before the handshake resolves a peer uid all share the reserved
        /// <see cref="PreHandshakeOverflowUid"/> bucket.
        /// </summary>
        private readonly ConcurrentDictionary<long, bool> _ordinaryOverflowDropLogged = new();

        /// <summary>
        /// Reserved uid bucket for <see cref="ShouldLogOrdinaryOverflowDrop"/> calls that occur
        /// before this association's handshake has resolved a real peer uid.
        /// </summary>
        private const long PreHandshakeOverflowUid = long.MinValue;

        public Association(
            Address remoteAddress,
            int outboundQueueCapacity = DefaultOutboundQueueCapacity,
            int controlQueueCapacity = DefaultControlQueueCapacity)
        {
            RemoteAddress = remoteAddress;
            _state = AssociationState.Create();
            _outboundChannel = Channel.CreateBounded<IOutboundEnvelope>(new BoundedChannelOptions(outboundQueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
            _controlChannel = Channel.CreateBounded<IOutboundEnvelope>(new BoundedChannelOptions(controlQueueCapacity)
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
        /// The reading side of this association's bounded ORDINARY outbound queue. Consumed by
        /// exactly one materialized outbound stream (<see cref="Akka.Streams.Dsl.ChannelSource.FromReader{T}"/>),
        /// per <see cref="EnsureOutboundMaterialized"/>.
        /// </summary>
        public ChannelReader<IOutboundEnvelope> OutboundReader => _outboundChannel.Reader;

        /// <summary>
        /// The reading side of this association's bounded CONTROL outbound queue -- separate
        /// infrastructure from <see cref="OutboundReader"/> (task 6.1). Consumed by exactly one
        /// materialized outbound stream, per <see cref="EnsureControlOutboundMaterialized"/>.
        /// </summary>
        public ChannelReader<IOutboundEnvelope> ControlReader => _controlChannel.Reader;

        /// <summary>
        /// Whether <see cref="EnsureOutboundMaterialized"/> has already started (or finished)
        /// materializing this association's ORDINARY outbound stream. A cheap check callers can
        /// use to skip allocating a materialize callback on the (post-first-call) steady-state path.
        /// </summary>
        public bool IsOutboundMaterialized => _outboundGate.IsStarted;

        /// <summary>
        /// Whether <see cref="EnsureControlOutboundMaterialized"/> has already started (or
        /// finished) materializing this association's CONTROL outbound stream.
        /// </summary>
        public bool IsControlOutboundMaterialized => _controlGate.IsStarted;

        /// <summary>
        /// Attempts to enqueue <paramref name="element"/> for the ORDINARY outbound stream to send.
        /// Non-blocking (<see cref="ChannelWriter{T}.TryWrite"/>) -- NEVER awaits/blocks a producing
        /// actor thread on a slow remote (Decision 7). Returns <see langword="false"/> when the
        /// bounded queue is full; the caller (<c>ArteryRemoting</c>) applies the overflow policy
        /// (ordinary messages -> dead letters).
        /// </summary>
        public bool TryEnqueueOutbound(IOutboundEnvelope element) => _outboundChannel.Writer.TryWrite(element);

        /// <summary>
        /// Attempts to enqueue <paramref name="element"/> for the CONTROL outbound stream to send.
        /// Non-blocking, same discipline as <see cref="TryEnqueueOutbound"/>. See the type-level
        /// remarks on <see cref="DefaultControlQueueCapacity"/> for the GROUP7 overflow-policy note.
        /// </summary>
        public bool TryEnqueueControl(IOutboundEnvelope element) => _controlChannel.Writer.TryWrite(element);

        /// <summary>
        /// Ensures this association's ORDINARY outbound stream is materialized exactly once, no
        /// matter how many threads call this concurrently. The callback is supplied by the
        /// transport (<c>ArteryRemoting</c>), which owns the Tcp extension / materializer / settings
        /// this pure state type deliberately does not know about.
        /// </summary>
        public void EnsureOutboundMaterialized(Action<Association> materialize) =>
            _outboundGate.EnsureStarted(() => materialize(this));

        /// <summary>
        /// Ensures this association's CONTROL outbound stream is materialized exactly once, no
        /// matter how many threads call this concurrently. See <see cref="EnsureOutboundMaterialized"/>.
        /// </summary>
        public void EnsureControlOutboundMaterialized(Action<Association> materialize) =>
            _controlGate.EnsureStarted(() => materialize(this));

        /// <summary>
        /// Marks the ORDINARY outbound channel complete (no further writes accepted) so its
        /// materialized <see cref="Akka.Streams.Dsl.ChannelSource.FromReader{T}"/> consumer
        /// finishes gracefully. Called on transport shutdown.
        /// </summary>
        public void CompleteOutbound() => _outboundChannel.Writer.TryComplete();

        /// <summary>
        /// Marks the CONTROL outbound channel complete. See <see cref="CompleteOutbound"/>.
        /// </summary>
        public void CompleteControlOutbound() => _controlChannel.Writer.TryComplete();

        /// <summary>
        /// Records (task 6.6: "log once per association, not per message") that a quarantine-drop
        /// warning has been logged for <paramref name="uid"/>. Returns <see langword="true"/> the
        /// FIRST time it is called for a given uid (the caller should log), and
        /// <see langword="false"/> every subsequent call for that same uid (the caller should stay
        /// silent).
        /// </summary>
        public bool ShouldLogQuarantineDrop(long uid) => _quarantineDropLogged.TryAdd(uid, true);

        /// <summary>
        /// Records ("log once per association, not per message" -- same discipline as
        /// <see cref="ShouldLogQuarantineDrop"/>) that an ORDINARY outbound queue overflow-drop
        /// warning has been logged for <paramref name="uid"/> (or, pre-handshake, the reserved
        /// <see cref="PreHandshakeOverflowUid"/> bucket). Returns <see langword="true"/> the FIRST
        /// time it is called for a given uid (the caller should log), and <see langword="false"/>
        /// every subsequent call for that same uid (the caller should stay silent and just drop).
        /// </summary>
        public bool ShouldLogOrdinaryOverflowDrop(long? uid) =>
            _ordinaryOverflowDropLogged.TryAdd(uid ?? PreHandshakeOverflowUid, true);

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
