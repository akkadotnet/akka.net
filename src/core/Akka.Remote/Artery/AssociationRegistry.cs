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
using Akka.Streams;

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
        private volatile bool _hasEverRestarted;

        /// <summary>
        /// Whether <see cref="EnsureStarted"/> has already started (or finished) materializing.
        /// A cheap check callers can use to skip allocating a materialize callback on the
        /// (post-first-call) steady-state path.
        /// </summary>
        public bool IsStarted => Volatile.Read(ref _started) != 0;

        /// <summary>
        /// Whether <see cref="Reset"/> has EVER been called on this gate -- i.e. whether the
        /// stream this gate guards has restarted (design.md group 9) at least once. Sticky for
        /// the gate's whole lifetime (never cleared back to <see langword="false"/>): once a
        /// stream has restarted even once, EVERY subsequent materialization -- regardless of
        /// which caller's <see cref="EnsureStarted"/> call happens to win the race to actually
        /// run it (the scheduled restart callback in <c>ArteryRemoting.ScheduleOutboundRestart</c>,
        /// or an ordinary producer's on-demand <c>EnsureOutboundMaterialized</c> call arriving in
        /// the same window) -- must be treated as a reconnect for handshake-safety purposes (see
        /// <see cref="OutboundHandshakeStage.ForceReqOnStart"/>'s remarks): the peer on the other
        /// end of ANY given materialization from here on could have restarted under a new uid.
        /// </summary>
        public bool HasEverRestarted => _hasEverRestarted;

        /// <summary>
        /// Runs <paramref name="materialize"/> exactly once, no matter how many threads call this
        /// concurrently -- only the FIRST caller's callback executes (CAS-gated on an internal flag).
        ///
        /// <para>
        /// <b>A throwing callback resets the gate instead of stranding it.</b> <c>_started</c> is
        /// CAS-flipped to 1 BEFORE <paramref name="materialize"/> runs, so if the callback throws
        /// (any exception NOT already caught/swallowed inside it -- e.g. a genuine bug, or a
        /// transient failure unrelated to the specific shutdown races <c>ArteryRemoting.MaterializeOutboundStream</c>
        /// already catches and swallows internally), the gate would otherwise stay latched
        /// "started" forever with no stream ever actually materialized and no restart ever
        /// scheduled for it. Resetting on throw (then rethrowing, so the caller still observes the
        /// original failure) lets the NEXT <see cref="EnsureStarted"/> call try again instead.
        /// </para>
        /// </summary>
        public void EnsureStarted(Action materialize)
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
                return;

            try
            {
                materialize();
            }
            catch
            {
                Volatile.Write(ref _started, 0);
                throw;
            }
        }

        /// <summary>
        /// Resets the latch so the NEXT <see cref="EnsureStarted"/> call materializes again --
        /// design.md group 9, "Association outbound-stream lifecycle: reconnect": called once an
        /// outbound stream's completion is observed, so re-materialization can be scheduled after
        /// <c>outbound-restart-backoff</c>. Not CAS-guarded: by construction, only the single
        /// completion continuation for the stream this gate belongs to ever calls this (stream
        /// materializations for a given gate are strictly sequential -- the gate itself is what
        /// prevents two from ever running concurrently). Also latches <see cref="HasEverRestarted"/>.
        /// </summary>
        public void Reset()
        {
            _hasEverRestarted = true;
            Volatile.Write(ref _started, 0);
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
        /// Default capacity for <see cref="ControlReader"/>'s bounded channel -- matches Pekko's
        /// <c>outbound-control-queue-size</c> default. Control traffic (handshake, heartbeat,
        /// quarantine notice, reliable system-message envelopes and their Ack/Nack replies) is
        /// low-volume relative to ordinary user traffic in steady state (design.md task group 6,
        /// task 6.1), but a mass-termination burst (e.g. a large `Terminate()`/CoordinatedShutdown
        /// fanning out thousands of `Unwatch` system messages at once) can legitimately need a MUCH
        /// larger buffer than the old 256 -- that undersized default caused spurious quarantines
        /// against an otherwise healthy peer under exactly that burst. The full asymmetric overflow
        /// policy (control overflow -> quarantine, per Decision 7) is implemented -- see the
        /// type-level "GROUP7 RESOLVED" remarks. Overridable via
        /// <c>akka.remote.artery.advanced.outbound-control-queue-size</c> (<see cref="ArterySettings.OutboundControlQueueSize"/>).
        /// </summary>
        public const int DefaultControlQueueCapacity = 20_000;

        /// <summary>
        /// Default capacity for <see cref="LargeReader"/>'s bounded channel (task 10.2, "Large
        /// Message Stream") -- matches Pekko's <c>outbound-large-message-queue-size</c> default.
        /// This channel is allocated unconditionally, exactly like <see cref="_outboundChannel"/>/
        /// <see cref="_controlChannel"/> (task group 6, task 6.1's "factor the shared shape"
        /// precedent) -- whether it is EVER materialized/enqueued to is entirely a transport-layer
        /// decision (<c>ArteryRemoting</c> only routes to it, and only ever calls
        /// <see cref="EnsureLargeOutboundMaterialized"/>, when <see cref="ArterySettings.LargeMessageChannelEnabled"/>
        /// is <see langword="true"/>) -- see design.md task 10.2's gate L remarks for why an
        /// always-allocated-but-never-used bounded channel is behavior-identical to today when the
        /// feature is off. Overridable via
        /// <c>akka.remote.artery.advanced.outbound-large-message-queue-size</c> (<see cref="ArterySettings.OutboundLargeMessageQueueSize"/>).
        /// </summary>
        public const int DefaultLargeQueueCapacity = 256;

        private volatile AssociationState _state;

        // Typed as the IOutboundEnvelope INTERFACE (not the concrete OutboundEnvelope record) so
        // ChannelSource.FromReader(OutboundReader) yields a Source<IOutboundEnvelope, _> that feeds
        // OutboundHandshakeStage's Inlet<IOutboundEnvelope> directly -- no upcast .Select needed
        // between the channel and the handshake stage (design.md Decision 9's ChannelSource.FromReader
        // mandate; see the G3 opening-refactor task report for why the interface, not the concrete
        // type, is the channel's type parameter).
        private readonly Channel<IOutboundEnvelope> _outboundChannel;
        private readonly Channel<IOutboundEnvelope> _controlChannel;

        /// <summary>
        /// Bounded channel for the LARGE-MESSAGE outbound stream (task 10.2). Allocated
        /// unconditionally -- see <see cref="DefaultLargeQueueCapacity"/>'s remarks for why an
        /// always-allocated-but-unused channel is harmless when the feature is disabled.
        /// </summary>
        private readonly Channel<IOutboundEnvelope> _largeChannel;

        private readonly MaterializeOnceGate _outboundGate = new();
        private readonly MaterializeOnceGate _controlGate = new();
        private readonly MaterializeOnceGate _largeGate = new();

        /// <summary>
        /// The <see cref="UniqueKillSwitch"/> of the CURRENT materialization of this association's
        /// ORDINARY outbound stream (design.md group 9, canonical reconnect fix), or
        /// <see langword="null"/> if that stream has never been materialized. Published by
        /// <see cref="SetOutboundKillSwitch"/> on the materializing thread; read + tripped by
        /// <see cref="TripOutboundKillSwitch"/> from the CONTROL stream's termination continuation
        /// on a different thread -- <c>volatile</c> for that cross-thread visibility (a reference
        /// write/read is already atomic).
        ///
        /// <para>
        /// <b>Why the control stream trips it.</b> The ordinary stream has no keep-alive, so it
        /// detects a dead peer only when an ordinary write happens to fail -- and a single write to
        /// a just-gracefully-closed socket can succeed locally (the peer's RST lands only
        /// afterwards), leaving an idle ordinary stream stranded on a dead connection indefinitely.
        /// The CONTROL stream detects the same peer death RELIABLY -- its periodic heartbeat always
        /// produces a "second write" that hits the errored socket -- so when control's own
        /// connection genuinely fails we trip this switch to drive the ordinary stream down too, so
        /// it reconnects to the live incarnation alongside control instead of lingering. Tripping is
        /// idempotent (<see cref="UniqueKillSwitch.Shutdown"/>) and null-safe; a stale switch
        /// (ordinary already torn down / in restart-backoff) is a harmless no-op.
        /// </para>
        ///
        /// <para>
        /// <b>Edge-triggered, once per death (<see cref="MarkControlHealthy"/>/<see cref="TryConsumeControlHealthy"/>).</b>
        /// The trip fires only on control's transition from CONNECTED to FAILED -- NOT on every
        /// control-stream fault. Control reconnect-loops against a still-dead peer (each attempt is a
        /// fast connection-refused fault), and tripping the ordinary stream on each of those would
        /// churn it: a trip landing while the ordinary consumer is mid-handshake against the revived
        /// peer would drop its single held <c>pendingMessage</c> (accepted at-most-once, but
        /// needless). So the trip is gated on <see cref="TryConsumeControlHealthy"/>: it fires once
        /// for the initial death (control HAD connected -- warmup, or a prior incarnation), then goes
        /// quiet until control successfully connects again (<see cref="MarkControlHealthy"/>). After
        /// that single kick the ordinary stream self-manages its own reconnect loop and, once the
        /// peer is back, delivers its still-queued messages in order UNINTERRUPTED. Non-lossy for
        /// pinned invariant 5: anything still in the association-owned channel survives (the channel
        /// is externally owned and outlives any single materialization).
        /// </para>
        /// </summary>
        private volatile UniqueKillSwitch? _outboundKillSwitch;

        /// <summary>
        /// The <see cref="UniqueKillSwitch"/> of the CURRENT materialization of this association's
        /// LARGE-MESSAGE outbound stream (task 10.2), or <see langword="null"/> if that stream has
        /// never been materialized. Tripped alongside <see cref="_outboundKillSwitch"/> by
        /// <see cref="TripLargeOutboundKillSwitch"/> for the exact same reason
        /// <see cref="_outboundKillSwitch"/> is tripped from the CONTROL stream's termination
        /// continuation -- the large stream has no heartbeat of its own either, so it can only
        /// detect a dead peer when an outbound write happens to fail. Null-safe/idempotent to
        /// trip even when the large stream was never materialized (feature disabled, or not yet
        /// used for this association).
        /// </summary>
        private volatile UniqueKillSwitch? _largeOutboundKillSwitch;

        /// <summary>
        /// Edge-detector state for <see cref="_outboundKillSwitch"/>'s once-per-death tripping. Set
        /// to 1 by <see cref="MarkControlHealthy"/> when the CONTROL stream's outbound connection is
        /// successfully ESTABLISHED (its <c>OutgoingConnection</c> materialized task completes -- a
        /// connection-refused reconnect attempt against a dead peer FAULTS that task instead, so it
        /// never marks healthy); atomically read-and-cleared to 0 by <see cref="TryConsumeControlHealthy"/>
        /// on the control stream's fault, which trips the ordinary stream only if control had in fact
        /// been connected since the last trip. <see cref="Interlocked"/> throughout -- written on the
        /// connection-established continuation, consumed on the termination continuation, different
        /// threads.
        /// </summary>
        private int _controlHealthy;

        /// <summary>
        /// Set (independently per stream) by <see cref="CompleteOutbound"/>/<see cref="CompleteControlOutbound"/>
        /// -- design.md group 9's restart guard: "no restart after transport <c>Shutdown()</c>".
        /// <see cref="ArteryRemoting.Shutdown"/> is the only production caller of either
        /// <c>Complete*Outbound</c> method, so observing either flag set means this association's
        /// stream was deliberately torn down for good, and the outbound-stream-termination
        /// continuation must not schedule a restart for it.
        /// </summary>
        private volatile bool _outboundShutDown;
        private volatile bool _controlShutDown;

        /// <summary>
        /// Set by <see cref="CompleteLargeOutbound"/> -- the LARGE-MESSAGE analog of
        /// <see cref="_outboundShutDown"/>/<see cref="_controlShutDown"/>. See <see cref="IsOutboundShutDown"/>.
        /// </summary>
        private volatile bool _largeShutDown;

        /// <summary>
        /// Association-owned state for the OUTBOUND half of reliable system-message delivery
        /// (design.md gate G3's <see cref="SystemMessageDeliveryStage"/>, extended by design.md
        /// group 9's reconnect invariant 3: "no unacked system message may be lost across a
        /// restart"). Deliberately NOT owned by the stage's per-materialization
        /// <c>GraphStageLogic</c> -- a fresh materialization (stream restart) attaches to this
        /// SAME instance instead of starting from an empty buffer, so in-flight unacknowledged
        /// system messages survive the restart. See <see cref="SystemMessageDeliveryState"/>.
        /// </summary>
        private readonly SystemMessageDeliveryState _systemMessageDeliveryState = new();

        /// <summary>
        /// Per-uid "have we already logged a quarantine-drop for this uid" latch (task 6.6:
        /// "log once per association, not per message"). Keyed by uid (not just a single
        /// per-association flag) so a NEW incarnation -- a different uid, possibly quarantined
        /// again in the future -- gets its own fresh unlogged state.
        /// </summary>
        private readonly ConcurrentDictionary<long, bool> _quarantineDropLogged = new();

        public Association(
            Address remoteAddress,
            int outboundQueueCapacity = DefaultOutboundQueueCapacity,
            int controlQueueCapacity = DefaultControlQueueCapacity,
            int largeQueueCapacity = DefaultLargeQueueCapacity)
        {
            RemoteAddress = remoteAddress;
            OutboundQueueCapacity = outboundQueueCapacity;
            ControlQueueCapacity = controlQueueCapacity;
            LargeQueueCapacity = largeQueueCapacity;
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
            _largeChannel = Channel.CreateBounded<IOutboundEnvelope>(new BoundedChannelOptions(largeQueueCapacity)
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
        /// This association's ORDINARY outbound queue capacity, as constructed (see
        /// <see cref="DefaultOutboundQueueCapacity"/>). Exposed so callers (<c>ArteryRemoting</c>'s
        /// overflow log messages) report the ACTUAL configured capacity rather than a hardcoded
        /// constant -- tests that pass a custom capacity would otherwise see a wrong number in the
        /// log.
        /// </summary>
        public int OutboundQueueCapacity { get; }

        /// <summary>
        /// This association's CONTROL outbound queue capacity, as constructed. See
        /// <see cref="OutboundQueueCapacity"/>.
        /// </summary>
        public int ControlQueueCapacity { get; }

        /// <summary>
        /// This association's LARGE-MESSAGE outbound queue capacity, as constructed (task 10.2).
        /// See <see cref="OutboundQueueCapacity"/>.
        /// </summary>
        public int LargeQueueCapacity { get; }

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
        /// The reading side of this association's bounded LARGE-MESSAGE outbound queue (task
        /// 10.2) -- separate infrastructure from <see cref="OutboundReader"/>/<see cref="ControlReader"/>.
        /// Consumed by exactly one materialized outbound stream, per <see cref="EnsureLargeOutboundMaterialized"/>
        /// -- only ever materialized by <c>ArteryRemoting</c> when <see cref="ArterySettings.LargeMessageChannelEnabled"/>
        /// is <see langword="true"/>.
        /// </summary>
        public ChannelReader<IOutboundEnvelope> LargeReader => _largeChannel.Reader;

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
        /// Whether <see cref="EnsureLargeOutboundMaterialized"/> has already started (or
        /// finished) materializing this association's LARGE-MESSAGE outbound stream (task 10.2).
        /// </summary>
        public bool IsLargeOutboundMaterialized => _largeGate.IsStarted;

        /// <summary>
        /// Whether the ORDINARY outbound stream has EVER restarted (design.md group 9) -- see
        /// <see cref="MaterializeOnceGate.HasEverRestarted"/>. Every caller that materializes this
        /// stream (whether <c>ArteryRemoting.ScheduleOutboundRestart</c>'s scheduled callback, or
        /// an ordinary producer's own on-demand <c>EnsureOutboundMaterialized</c> call racing it)
        /// consults this at the moment its callback actually runs, so whichever one wins gets the
        /// SAME correct answer -- see <c>ArteryRemoting.MaterializeOutboundStream</c>'s
        /// <c>isRestart</c> parameter.
        /// </summary>
        public bool HasOutboundEverRestarted => _outboundGate.HasEverRestarted;

        /// <summary>
        /// Whether the CONTROL outbound stream has EVER restarted. See <see cref="HasOutboundEverRestarted"/>.
        /// </summary>
        public bool HasControlEverRestarted => _controlGate.HasEverRestarted;

        /// <summary>
        /// Whether the LARGE-MESSAGE outbound stream has EVER restarted (task 10.2). See
        /// <see cref="HasOutboundEverRestarted"/>.
        /// </summary>
        public bool HasLargeEverRestarted => _largeGate.HasEverRestarted;

        /// <summary>
        /// Association-owned state backing <see cref="SystemMessageDeliveryStage"/>'s outbound
        /// unacknowledged buffer/seqNo/incarnation tracking -- see the type-level remarks on
        /// <see cref="_systemMessageDeliveryState"/>.
        /// </summary>
        public SystemMessageDeliveryState SystemMessageDeliveryState => _systemMessageDeliveryState;

        /// <summary>
        /// Whether this association's ORDINARY outbound stream has been permanently torn down by
        /// <see cref="CompleteOutbound"/> (transport <see cref="ArteryRemoting.Shutdown"/>) -- design.md
        /// group 9's restart guard.
        /// </summary>
        public bool IsOutboundShutDown => _outboundShutDown;

        /// <summary>
        /// Whether this association's CONTROL outbound stream has been permanently torn down by
        /// <see cref="CompleteControlOutbound"/>. See <see cref="IsOutboundShutDown"/>.
        /// </summary>
        public bool IsControlShutDown => _controlShutDown;

        /// <summary>
        /// Whether this association's LARGE-MESSAGE outbound stream has been permanently torn
        /// down by <see cref="CompleteLargeOutbound"/> (task 10.2). See <see cref="IsOutboundShutDown"/>.
        /// </summary>
        public bool IsLargeShutDown => _largeShutDown;

        /// <summary>
        /// Resets the ORDINARY outbound stream's materialize-once gate (design.md group 9) so the
        /// next <see cref="EnsureOutboundMaterialized"/> call re-materializes it.
        /// </summary>
        public void ResetOutboundGate() => _outboundGate.Reset();

        /// <summary>
        /// Resets the CONTROL outbound stream's materialize-once gate. See <see cref="ResetOutboundGate"/>.
        /// </summary>
        public void ResetControlGate() => _controlGate.Reset();

        /// <summary>
        /// Resets the LARGE-MESSAGE outbound stream's materialize-once gate (task 10.2). See
        /// <see cref="ResetOutboundGate"/>.
        /// </summary>
        public void ResetLargeGate() => _largeGate.Reset();

        /// <summary>
        /// Publishes the <see cref="UniqueKillSwitch"/> for the ORDINARY outbound stream's current
        /// materialization so <see cref="TripOutboundKillSwitch"/> can later abort it. Called by the
        /// transport (<c>ArteryRemoting.MaterializeOutboundStream</c>) each time the ordinary stream
        /// is (re-)materialized -- see <see cref="_outboundKillSwitch"/>.
        /// </summary>
        public void SetOutboundKillSwitch(UniqueKillSwitch killSwitch) => _outboundKillSwitch = killSwitch;

        /// <summary>
        /// Drives the ORDINARY outbound stream's current materialization down (if any) via its
        /// <see cref="UniqueKillSwitch"/>, so the standard termination -&gt;
        /// <c>ArteryRemoting.ScheduleOutboundRestart</c> path reconnects it. Idempotent + null-safe.
        /// Called from the CONTROL stream's termination continuation when control's connection
        /// genuinely fails AND had previously connected (<see cref="TryConsumeControlHealthy"/>) --
        /// see <see cref="_outboundKillSwitch"/> for why control's reliable death detection drives
        /// the keep-alive-less ordinary stream, and why the trip is edge-triggered.
        /// </summary>
        public void TripOutboundKillSwitch() => _outboundKillSwitch?.Shutdown();

        /// <summary>
        /// Publishes the <see cref="UniqueKillSwitch"/> for the LARGE-MESSAGE outbound stream's
        /// current materialization (task 10.2). See <see cref="SetOutboundKillSwitch"/>.
        /// </summary>
        public void SetLargeOutboundKillSwitch(UniqueKillSwitch killSwitch) => _largeOutboundKillSwitch = killSwitch;

        /// <summary>
        /// Drives the LARGE-MESSAGE outbound stream's current materialization down (if any), for
        /// the exact same reason and from the exact same call site as <see cref="TripOutboundKillSwitch"/>
        /// (task 10.2) -- see <see cref="_largeOutboundKillSwitch"/>.
        /// </summary>
        public void TripLargeOutboundKillSwitch() => _largeOutboundKillSwitch?.Shutdown();

        /// <summary>
        /// Records that the CONTROL stream's outbound connection has been successfully ESTABLISHED,
        /// arming the once-per-death ordinary-stream trip (see <see cref="_controlHealthy"/>). Called
        /// from the transport when control's <c>OutgoingConnection</c> materialized task completes.
        /// </summary>
        public void MarkControlHealthy() => Interlocked.Exchange(ref _controlHealthy, 1);

        /// <summary>
        /// Atomically reports whether the CONTROL stream had connected since the last trip, clearing
        /// the flag so a subsequent reconnect-loop fault (against a still-dead peer, which never
        /// re-armed via <see cref="MarkControlHealthy"/>) does NOT trip the ordinary stream again.
        /// See <see cref="_controlHealthy"/> / <see cref="_outboundKillSwitch"/>.
        /// </summary>
        public bool TryConsumeControlHealthy() => Interlocked.Exchange(ref _controlHealthy, 0) == 1;

        /// <summary>
        /// Whether the ORDINARY outbound stream should be (re-)materialized right now (design.md
        /// group 9): <see langword="false"/> once this stream has been shut down for good
        /// (<see cref="IsOutboundShutDown"/>), OR while the CURRENT peer uid is quarantined --
        /// <see cref="RemoteTransport.Send"/> already gates every ordinary send for a quarantined
        /// uid, so reconnecting the ordinary stream in that state would only waste a connection
        /// (design.md: "ordinary remains gated at Send()"). A stale-uid quarantine (a PRIOR,
        /// superseded incarnation) does not count -- only the CURRENT uid's quarantine status
        /// matters, so a genuinely new incarnation (a different, non-quarantined uid) is free to
        /// reconnect normally.
        /// </summary>
        public bool ShouldRestartOutbound() =>
            !_outboundShutDown && !(CurrentState.UniqueRemoteAddress is { } peer && IsQuarantined(peer.Uid));

        /// <summary>
        /// Whether the CONTROL outbound stream should be (re-)materialized right now. Unlike
        /// <see cref="ShouldRestartOutbound"/>, quarantine status is irrelevant here -- the
        /// control stream restarts regardless of quarantine (design.md: "quarantined associations
        /// still restart their CONTROL stream -- piercing requires a live control channel"). The
        /// only thing that ever permanently stops it is this stream's own shutdown.
        /// </summary>
        public bool ShouldRestartControl() => !_controlShutDown;

        /// <summary>
        /// Whether the LARGE-MESSAGE outbound stream should be (re-)materialized right now (task
        /// 10.2). Same rule as <see cref="ShouldRestartOutbound"/> -- large-message traffic is
        /// ordinary USER data (just isolated onto its own stream), so it is gated by quarantine
        /// exactly the same way.
        /// </summary>
        public bool ShouldRestartLargeOutbound() =>
            !_largeShutDown && !(CurrentState.UniqueRemoteAddress is { } peer && IsQuarantined(peer.Uid));

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
        /// Attempts to enqueue <paramref name="element"/> for the LARGE-MESSAGE outbound stream to
        /// send (task 10.2). Non-blocking, same discipline as <see cref="TryEnqueueOutbound"/>.
        /// Overflow is a soft drop (<c>ArteryRemoting</c> publishes <see cref="Akka.Event.Dropped"/>),
        /// never a quarantine -- large-message traffic follows the ORDINARY overflow policy, not
        /// control's.
        /// </summary>
        public bool TryEnqueueLarge(IOutboundEnvelope element) => _largeChannel.Writer.TryWrite(element);

        /// <summary>
        /// The number of elements CURRENTLY buffered in the ORDINARY outbound channel, awaiting a
        /// consumer. Test-observability surface added for design.md task 8.5 ("slow receiver tests
        /// proving queues do not grow unbounded") -- <c>System.Threading.Channels</c>' bounded
        /// channel implementation supports O(1) <see cref="ChannelReader{T}.Count"/>, so this is
        /// cheap enough to sample repeatedly from a test without perturbing the property under
        /// test. Production code has no need of this (the bounded <see cref="TryEnqueueOutbound"/> /
        /// dead-letter-on-<see langword="false"/> pattern is capacity-agnostic), so this exists
        /// purely for tests to assert the bound is never exceeded.
        /// </summary>
        public int OutboundQueueCount => _outboundChannel.Reader.Count;

        /// <summary>
        /// The CONTROL-channel analog of <see cref="OutboundQueueCount"/>. See its remarks.
        /// </summary>
        public int ControlQueueCount => _controlChannel.Reader.Count;

        /// <summary>
        /// The LARGE-MESSAGE-channel analog of <see cref="OutboundQueueCount"/> (task 10.2). See
        /// its remarks.
        /// </summary>
        public int LargeQueueCount => _largeChannel.Reader.Count;

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
        /// Ensures this association's LARGE-MESSAGE outbound stream is materialized exactly once
        /// (task 10.2), no matter how many threads call this concurrently. See
        /// <see cref="EnsureOutboundMaterialized"/>. Only ever called by <c>ArteryRemoting</c>
        /// when <see cref="ArterySettings.LargeMessageChannelEnabled"/> is <see langword="true"/>.
        /// </summary>
        public void EnsureLargeOutboundMaterialized(Action<Association> materialize) =>
            _largeGate.EnsureStarted(() => materialize(this));

        /// <summary>
        /// Marks the ORDINARY outbound channel complete (no further writes accepted) so its
        /// materialized <see cref="Akka.Streams.Dsl.ChannelSource.FromReader{T}"/> consumer
        /// finishes gracefully. Called on transport shutdown -- also latches
        /// <see cref="IsOutboundShutDown"/> so design.md group 9's reconnect logic never
        /// re-materializes this stream again.
        /// </summary>
        public void CompleteOutbound()
        {
            _outboundChannel.Writer.TryComplete();
            _outboundShutDown = true;
        }

        /// <summary>
        /// Marks the CONTROL outbound channel complete. See <see cref="CompleteOutbound"/>.
        /// </summary>
        public void CompleteControlOutbound()
        {
            _controlChannel.Writer.TryComplete();
            _controlShutDown = true;
        }

        /// <summary>
        /// Marks the LARGE-MESSAGE outbound channel complete (task 10.2). See
        /// <see cref="CompleteOutbound"/>. Safe to call unconditionally, whether or not the large
        /// stream was ever materialized for this association.
        /// </summary>
        public void CompleteLargeOutbound()
        {
            _largeChannel.Writer.TryComplete();
            _largeShutDown = true;
        }

        /// <summary>
        /// Records (task 6.6: "log once per association, not per message") that a quarantine-drop
        /// warning has been logged for <paramref name="uid"/>. Returns <see langword="true"/> the
        /// FIRST time it is called for a given uid (the caller should log), and
        /// <see langword="false"/> every subsequent call for that same uid (the caller should stay
        /// silent).
        /// </summary>
        public bool ShouldLogQuarantineDrop(long uid) => _quarantineDropLogged.TryAdd(uid, true);

        /// <summary>
        /// Monotonically incremented on EVERY <see cref="CompleteHandshake"/> call, regardless of
        /// whether it actually changed <see cref="AssociationState"/> (a same-uid
        /// <see cref="HandshakeRsp"/> is a documented, reference-equal no-op on
        /// <see cref="Artery.AssociationState.CompleteHandshake"/> -- see that method's remarks and
        /// <c>AssociationStateSpec</c>'s idempotency test, both left INTENTIONALLY unchanged here).
        /// Exists purely so <see cref="OutboundHandshakeStage"/>'s <c>ForceReqOnStart</c> path
        /// (design.md group 9) can detect "a fresh handshake round-trip was processed since MY
        /// materialization started" even in the same-uid case, where <see cref="AssociationState"/>
        /// itself provides no observable signal at all (see <see cref="IOutboundContext.HandshakeGeneration"/>'s
        /// remarks for the full rationale).
        /// </summary>
        private long _handshakeGeneration;

        /// <summary>See <see cref="_handshakeGeneration"/>.</summary>
        public long HandshakeGeneration => Interlocked.Read(ref _handshakeGeneration);

        /// <summary>
        /// CAS loop applying <see cref="AssociationState.CompleteHandshake"/>. Returns both the
        /// snapshot immediately before this call's effective transition and the resulting
        /// snapshot, so <see cref="AssociationRegistry"/> can tell — without a separate,
        /// racy read — whether (and from what uid) an incarnation change just happened.
        /// </summary>
        public (AssociationState Previous, AssociationState Updated) CompleteHandshake(UniqueAddress peer)
        {
            Interlocked.Increment(ref _handshakeGeneration);

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
        private readonly int _controlQueueCapacity;
        private readonly int _largeQueueCapacity;

        /// <summary>
        /// Initializes a new instance of the <see cref="AssociationRegistry"/> class.
        /// </summary>
        /// <param name="outboundQueueCapacity">
        /// Capacity of every materialized <see cref="Association"/>'s bounded ORDINARY outbound
        /// channel (see <see cref="Association.DefaultOutboundQueueCapacity"/>).
        /// </param>
        /// <param name="controlQueueCapacity">
        /// Capacity of every materialized <see cref="Association"/>'s bounded CONTROL outbound
        /// channel (see <see cref="Association.DefaultControlQueueCapacity"/>).
        /// </param>
        /// <param name="largeQueueCapacity">
        /// Capacity of every materialized <see cref="Association"/>'s bounded LARGE-MESSAGE
        /// outbound channel (task 10.2; see <see cref="Association.DefaultLargeQueueCapacity"/>).
        /// </param>
        public AssociationRegistry(
            int outboundQueueCapacity = Association.DefaultOutboundQueueCapacity,
            int controlQueueCapacity = Association.DefaultControlQueueCapacity,
            int largeQueueCapacity = Association.DefaultLargeQueueCapacity)
        {
            _outboundQueueCapacity = outboundQueueCapacity;
            _controlQueueCapacity = controlQueueCapacity;
            _largeQueueCapacity = largeQueueCapacity;
        }

        /// <summary>
        /// Returns the <see cref="Association"/> for <paramref name="remoteAddress"/>, creating
        /// it (via <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey, System.Func{TKey,TValue})"/>)
        /// if this is the first reference to that address.
        /// </summary>
        public Association AssociationFor(Address remoteAddress) =>
            _byAddress.GetOrAdd(remoteAddress,
                static (addr, caps) => new Association(addr, caps.Outbound, caps.Control, caps.Large),
                (Outbound: _outboundQueueCapacity, Control: _controlQueueCapacity, Large: _largeQueueCapacity));

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
