//-----------------------------------------------------------------------
// <copyright file="OutboundHandshakeStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Outbound half of the Artery handshake, faithful to
    /// <c>openspec/changes/artery-tcp-remoting/design.md</c>
    /// ("Handshake + association/UID (gate G2)"). A
    /// <c>GraphStage&lt;FlowShape&lt;IOutboundEnvelope, IOutboundEnvelope&gt;&gt;</c> -- the injected
    /// <see cref="HandshakeReq"/> is wrapped in an <see cref="OutboundEnvelope"/> (no sender/recipient
    /// path, <see cref="IOutboundEnvelope.IsControl"/> true) instead of flowing as a naked object.
    ///
    /// <para><b>States.</b> <c>Start</c> → <c>ReqInProgress</c> → <c>Completed</c>. On
    /// materialization (or the first relevant event) the stage injects a <see cref="HandshakeReq"/>
    /// downstream and holds the first user element that arrives from upstream
    /// (<c>_pendingMessage</c>) WITHOUT pulling further — user traffic queues behind the stage,
    /// it is never dropped. Once <c>Completed</c>, the held element (if any) is emitted, and the
    /// stage passes elements through transparently.</para>
    ///
    /// <para><b>Retry / timeout.</b> Uses <see cref="TimerGraphStageLogic"/> timers: a repeating
    /// retry timer resends the <see cref="HandshakeReq"/> at <c>handshake-retry-interval</c>
    /// while incomplete, and a one-shot timeout timer fails the stage with
    /// <see cref="HandshakeTimeoutException"/> after <c>handshake-timeout</c> if the handshake
    /// never completes (the association is expected to retry the outbound stream). Neither timer
    /// detects completion. See the next note.</para>
    ///
    /// <para><b>How the stage learns that the handshake completed.</b> The association pushes
    /// the news. On entering <c>ReqInProgress</c> the logic registers a <c>GetAsyncCallback</c>
    /// with the association (<see cref="IOutboundContext.SubscribeHandshakeStateChanged"/>). The
    /// inbound pipeline calls <see cref="AssociationRegistry.CompleteHandshake"/> or
    /// <see cref="AssociationRegistry.CompleteOutboundHandshake"/>, which fires that callback. The
    /// stage then re-checks the association snapshot against its own completion rule before it
    /// releases traffic. The stage also reads that snapshot at <c>OnPull</c>, <c>OnPush</c> and the
    /// retry tick. Those reads are opportunistic. None of them is the detection path.</para>
    ///
    /// <para><b>Why polling is not enough.</b> A gating stage holds one user element and stops
    /// pulling. It has pushed nothing, so downstream demand is still outstanding. No <c>OnPull</c>
    /// arrives and no <c>OnPush</c> arrives. That left the retry tick as the only wake-up, so the
    /// first message on a gated association waited up to one <c>handshake-retry-interval</c>
    /// (1s by default) after the peer had already answered. On a cluster join that message is the
    /// seed's Welcome to every peer. Measured join-to-Welcome latency was ~1.07s with polling and
    /// ~0.07s with the push wake-up.</para>
    ///
    /// <para><b><c>inject-handshake-interval</c> (liveness re-injection — simplified per the
    /// task).</b> After completion, the stage tracks the timestamp it last injected a
    /// <see cref="HandshakeReq"/>. When a user element next flows (<c>OnPush</c>) and more than
    /// <c>inject-handshake-interval</c> has elapsed since that timestamp, the stage injects
    /// another <see cref="HandshakeReq"/> ahead of it (holding that one element for the next
    /// pull) instead of passing it straight through. This is a periodic
    /// "re-inject on next flow if due" gate, NOT a true idle-then-resume detector (it does not
    /// distinguish "traffic paused, then resumed" from "traffic has been continuous the whole
    /// time, ~1s has just passed") — the task explicitly sanctions exactly this simplification
    /// ("track last-injection time; if a message flows and it's been &gt; inject-handshake-interval
    /// since the last injection, inject another ahead of it").</para>
    ///
    /// <para><b>What completion means (issue #8496).</b> Completion means the peer knows OUR
    /// uid. Only a <see cref="HandshakeRsp"/> answering a <see cref="HandshakeReq"/> of ours proves
    /// that. <see cref="Artery.AssociationState.UniqueRemoteAddress"/> does not: the inbound
    /// direction sets that field too. See <c>Logic.CanSkipOwnHandshake</c>.</para>
    ///
    /// <para><b>Control-channel routing (task group 6, "Control Stream", task 6.3).</b> This
    /// SAME stage class is materialized on EVERY outbound stream (control, ordinary, and later
    /// large) — "every stream handshakes" — but only ONE of them, the control stream, is the one
    /// whose <see cref="Out"/> IS the control connection. So <see cref="IsControlStream"/>
    /// (constructor parameter, default <see langword="true"/> for source compatibility with the
    /// pre-6.3 shape) toggles how an injected/re-injected <see cref="HandshakeReq"/> is actually
    /// dispatched: when <see langword="true"/>, unchanged from before — pushed inline onto this
    /// stage's own <see cref="Out"/> (which flows straight to the control connection). When
    /// <see langword="false"/> (the ordinary/large stream's instance), the Req is instead handed
    /// to <see cref="IOutboundContext.SendControl"/> — a side channel that enqueues onto the
    /// ASSOCIATION's separate control queue/connection — and this stage's own <see cref="Out"/>
    /// never carries a <see cref="HandshakeReq"/> element at all. Either way, the "hold the
    /// pending user element until completion" gating behavior is unchanged; only the Req's
    /// delivery path differs.</para>
    /// </summary>
    internal sealed class OutboundHandshakeStage : GraphStage<FlowShape<IOutboundEnvelope, IOutboundEnvelope>>
    {
        /// <summary>
        /// The lifecycle states this stage's logic moves through.
        /// </summary>
        private enum State : byte
        {
            Start = 0,
            ReqInProgress = 1,
            Completed = 2
        }

        private const string RetryTimerKey = "OutboundHandshake-Retry";
        private const string TimeoutTimerKey = "OutboundHandshake-Timeout";

        public OutboundHandshakeStage(
            IOutboundContext context,
            TimeSpan retryInterval,
            TimeSpan handshakeTimeout,
            TimeSpan injectHandshakeInterval,
            bool isControlStream = true,
            bool forceReqOnStart = false,
            ITimeProvider? timeProvider = null)
        {
            if (retryInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(retryInterval), retryInterval, "must be positive.");
            if (handshakeTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(handshakeTimeout), handshakeTimeout, "must be positive.");
            if (injectHandshakeInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(injectHandshakeInterval), injectHandshakeInterval, "must be positive.");

            Context = context;
            RetryInterval = retryInterval;
            HandshakeTimeout = handshakeTimeout;
            InjectHandshakeInterval = injectHandshakeInterval;
            IsControlStream = isControlStream;
            ForceReqOnStart = forceReqOnStart;
            TimeProvider = timeProvider;
            Shape = new FlowShape<IOutboundEnvelope, IOutboundEnvelope>(In, Out);
        }

        public IOutboundContext Context { get; }

        /// <summary>
        /// Clock for the Req-injection schedule (<c>handshake-retry-interval</c> and
        /// <c>inject-handshake-interval</c>). <see langword="null"/> resolves to the materializing
        /// system's scheduler when the stream starts. Typed as the base
        /// <see cref="ITimeProvider"/> so a virtual clock can drive these intervals in a test.
        /// </summary>
        public ITimeProvider? TimeProvider { get; }

        public TimeSpan RetryInterval { get; }
        public TimeSpan HandshakeTimeout { get; }
        public TimeSpan InjectHandshakeInterval { get; }

        /// <summary>
        /// <see langword="true"/> when this instance is materialized on the control stream
        /// itself (the default, preserving the pre-6.3 shape used by every existing test/caller
        /// that does not pass this parameter): an injected <see cref="HandshakeReq"/> is pushed
        /// inline onto <see cref="Out"/>. <see langword="false"/> for the ordinary/large stream's
        /// instance: the Req is routed via <see cref="IOutboundContext.SendControl"/> instead —
        /// see the type-level "Control-channel routing" remarks.
        /// </summary>
        public bool IsControlStream { get; }

        /// <summary>
        /// <see langword="true"/> when this materialization is a design.md group 9 RECONNECT
        /// (a fresh materialization after the previous outbound stream terminated), rather than an
        /// association's first-ever materialization. Forces <see cref="Logic.PreStart"/> to always
        /// go through <c>ReqInProgress</c> (send/await a fresh <see cref="HandshakeReq"/>) instead
        /// of the G2 fast-path shortcut that treats "an association already exists for this
        /// address" as "handshake already complete".
        ///
        /// <para>
        /// <b>Why the fast path is unsafe across a restart.</b> <see cref="Artery.AssociationState.UniqueRemoteAddress"/>
        /// matches by ADDRESS, not by CURRENT LIVE CONNECTION — after a peer restarts under a new
        /// uid, the association's cached state still shows the OLD uid until the fresh handshake
        /// completes. A reconnected stream that trusted the stale "already associated" state would
        /// start flowing ordinary traffic (or, on the control stream, wait out a full
        /// <c>control-heartbeat-interval</c> before ever re-injecting) toward a peer that has never
        /// actually handshaked THIS uid's connection — the new peer's <see cref="InboundHandshakeStage"/>
        /// would drop every such envelope as an unknown origin (design.md group 9's reconnect
        /// correctness suite is what surfaced this). Forcing a fresh Req on every restart is always
        /// safe regardless of whether the peer's uid actually changed — a same-uid <see cref="HandshakeRsp"/>
        /// is an idempotent no-op (see <see cref="Artery.AssociationState.CompleteHandshake"/>) — it
        /// only costs one extra round trip. The G2 fast path is preserved for a stream's FIRST-ever
        /// materialization (<see langword="false"/>, the default), where "another stream on this
        /// same association already completed OUR OWN handshake" is a legitimate, still-current
        /// signal -- see <c>Logic.CanSkipOwnHandshake</c> for what counts as that, post-#8496.
        /// </para>
        /// </summary>
        public bool ForceReqOnStart { get; }

        public Inlet<IOutboundEnvelope> In { get; } = new("OutboundHandshake.in");
        public Outlet<IOutboundEnvelope> Out { get; } = new("OutboundHandshake.out");

        public override FlowShape<IOutboundEnvelope, IOutboundEnvelope> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        private sealed class Logic : TimerGraphStageLogic, IInHandler, IOutHandler
        {
            private readonly OutboundHandshakeStage _stage;

            /// <summary>
            /// Non-null while this logic is registered with the association for handshake
            /// notifications: from <see cref="PreStart"/>'s <c>ReqInProgress</c> branch until
            /// completion or <see cref="PostStop"/>.
            /// </summary>
            private Action? _handshakeStateChangedCallback;

            private State _state = State.Start;
            private IOutboundEnvelope? _pendingMessage;

            /// <summary>
            /// When this logic last injected a <see cref="HandshakeReq"/>; <see langword="null"/>
            /// until the first one. Every read and write goes through <see cref="_timeProvider"/>,
            /// so the whole schedule moves together when the clock is virtual.
            /// </summary>
            private DateTimeOffset? _lastInject;

            /// <summary>
            /// Resolved once at <see cref="PreStart"/>: the stage's explicit clock, else the
            /// materializing system's scheduler.
            /// </summary>
            private ITimeProvider _timeProvider = null!;

            /// <summary>
            /// Only meaningful when <see cref="OutboundHandshakeStage.ForceReqOnStart"/> -- the
            /// <see cref="IOutboundContext.HandshakeGeneration"/> value observed at
            /// <see cref="PreStart"/>, BEFORE this materialization's own fresh
            /// <see cref="HandshakeReq"/> has had any chance to be answered. Completion is only
            /// recognized once the CURRENT generation has advanced PAST this baseline -- proving a
            /// handshake round-trip was processed AFTER this materialization started, not merely
            /// that "some association already exists for this address" (which could be stale
            /// leftover state from a peer that has since restarted -- see
            /// <see cref="OutboundHandshakeStage.ForceReqOnStart"/>'s remarks).
            /// </summary>
            private long _handshakeGenerationBaseline;

            public Logic(OutboundHandshakeStage stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public override void PreStart()
            {
                // Resolve the clock before anything stamps _lastInject.
                _timeProvider = _stage.TimeProvider
                                ?? (Materializer as ActorMaterializer)?.System.Scheduler
                                ?? throw new InvalidOperationException(
                                    "No ITimeProvider available: this stream was not materialized " +
                                    "by an ActorMaterializer, so pass one to the stage explicitly.");

                var state = _stage.Context.AssociationState;

                if (!_stage.ForceReqOnStart && CanSkipOwnHandshake(state))
                {
                    _state = State.Completed;
                    _lastInject = _timeProvider.Now;
                    return;
                }

                if (!_stage.ForceReqOnStart && !_stage.IsControlStream && !state.OutboundHandshakeCompleted &&
                    state.UniqueRemoteAddress is not null)
                {
                    // This is the issue #8496 ordering. The peer dialed us first, so we know its
                    // uid from its HandshakeReq. The peer does not know our uid. We send our own
                    // Req and hold traffic until it answers. Log it, so the ordering is visible in
                    // production logs.
                    Log.Debug(
                        "Outbound Artery stream to [{0}]: the peer's uid is known from an INBOUND handshake only, " +
                        "so this stream sends its own HandshakeReq and holds traffic until the peer answers.",
                        _stage.Context.RemoteAddress);
                }

                _handshakeGenerationBaseline = _stage.Context.HandshakeGeneration;
                _state = State.ReqInProgress;
                ScheduleRepeatedly(RetryTimerKey, _stage.RetryInterval);
                ScheduleOnce(TimeoutTimerKey, _stage.HandshakeTimeout);

                // Register first, then re-check. A completion can land between the snapshot read
                // at the top of this method and this registration. Registering first means either
                // the re-check below sees it or the callback delivers it. It cannot fall between
                // the two. That matters because a gating stage stops pulling, so nothing else
                // re-enters this logic until the retry tick.
                _handshakeStateChangedCallback = GetAsyncCallback(OnHandshakeStateChanged);
                _stage.Context.SubscribeHandshakeStateChanged(this, _handshakeStateChangedCallback);
                RefreshCompletionFromContext();
            }

            public override void PostStop()
            {
                // Streams restart against an association that outlives them, so a leaked
                // registration would accumulate. Invoking a stopped stage's async callback is
                // harmless on its own: the interpreter drops async input for a completed logic.
                // This is hygiene, not a race fix.
                UnsubscribeHandshakeStateChanged();
            }

            private void UnsubscribeHandshakeStateChanged()
            {
                if (_handshakeStateChangedCallback is null)
                    return;

                _handshakeStateChangedCallback = null;
                _stage.Context.UnsubscribeHandshakeStateChanged(this);
            }

            /// <summary>
            /// The association reports that its handshake state advanced. Re-check that state
            /// against this stream's own rule. An ordinary stream waiting for its Rsp is not
            /// satisfied by the peer's inbound Req, nor by a completion for a newer incarnation.
            /// </summary>
            private void OnHandshakeStateChanged()
            {
                RefreshCompletionFromContext();

                if (_state != State.Completed)
                    return;

                // If we have a pending user message that has not been sent yet, flush it now
                // that the handshake is done. This runs from the async callback. This is what
                // prevents the message loss.
                if (_pendingMessage is { } held && IsAvailable(_stage.Out))
                {
                    _pendingMessage = null;
                    Push(_stage.Out, held);
                    return;
                }

                if (_pendingMessage is null && !IsClosed(_stage.In) && !HasBeenPulled(_stage.In))
                    Pull(_stage.In);
            }

            /// <summary>
            /// Can this materialization skip the handshake and start out <c>Completed</c>?
            ///
            /// <para>
            /// If another outbound stream on this association has already handled the handshake, we
            /// can and must skip it. That covers a sibling lane when <c>outbound-lanes &gt; 1</c>.
            /// It also covers the control, ordinary and large streams of one association at the
            /// default single lane, and any later re-materialization of them.
            /// </para>
            ///
            /// <para>
            /// For an ordinary, large or lane stream, "already handled" means the peer answered a
            /// <see cref="HandshakeReq"/> of ours
            /// (<see cref="Artery.AssociationState.OutboundHandshakeCompleted"/>). A known
            /// <see cref="Artery.AssociationState.UniqueRemoteAddress"/> is not enough. The peer's
            /// own inbound Req sets that field, and it says nothing about whether the peer
            /// registered our uid. Trusting it sent our first user message into the peer's
            /// unknown-origin drop (issue #8496).
            /// </para>
            ///
            /// <para>
            /// If we are the control stream, we are sending and handling the handshake. Waiting on
            /// it would deadlock us: the <see cref="HandshakeRsp"/> we owe the peer sits in the
            /// same control queue we would be gating, so two systems that dial each other at the
            /// same instant would each hold the Rsp the other waits for. So we skip it. Skipping is
            /// also safe. The receiver drops unknown-origin ORDINARY envelopes only. It always
            /// dispatches control envelopes: handshake, heartbeat, quarantine notice, and system
            /// messages with their Ack/Nack.
            /// </para>
            /// </summary>
            private bool CanSkipOwnHandshake(AssociationState state) =>
                state.UniqueRemoteAddress is { } already &&
                Equals(already.Address, _stage.Context.RemoteAddress) &&
                (_stage.IsControlStream || state.OutboundHandshakeCompleted);

            public void OnPull()
            {
                RefreshCompletionFromContext();

                if (_state == State.Completed)
                {
                    if (_pendingMessage is { } held)
                    {
                        _pendingMessage = null;
                        Push(_stage.Out, held);
                        return;
                    }

                    if (IsClosed(_stage.In))
                    {
                        CompleteStage();
                        return;
                    }

                    if (!HasBeenPulled(_stage.In))
                        Pull(_stage.In);
                    return;
                }

                // Not completed: resend the Req if due (never drops the held element, if any).
                TryInjectReq();

                if (_pendingMessage is null && !IsClosed(_stage.In) && !HasBeenPulled(_stage.In))
                    Pull(_stage.In);
            }

            public void OnPush()
            {
                var elem = Grab(_stage.In);
                RefreshCompletionFromContext();

                if (_state != State.Completed)
                {
                    // Hold the element; never drop; never pull further while one is held.
                    _pendingMessage = elem;
                    return;
                }

                if (ShouldReinjectForLiveness())
                {
                    if (_stage.IsControlStream)
                    {
                        _pendingMessage = elem;

                        if (IsAvailable(_stage.Out))
                        {
                            _lastInject = _timeProvider.Now;
                            Push(_stage.Out, BuildReqEnvelope());
                        }

                        return;
                    }

                    // Non-control stream: the Req travels via the control side channel and never
                    // occupies this stream's Out slot, so the user element can flow through
                    // immediately below -- no need to hold it.
                    _lastInject = _timeProvider.Now;
                    _stage.Context.SendControl(BuildReqMessage());
                }

                if (IsAvailable(_stage.Out))
                    Push(_stage.Out, elem);
                else
                    _pendingMessage = elem; // defensive: should not normally happen, see stage remarks.
            }

            public void OnUpstreamFinish()
            {
                if (_pendingMessage is null)
                    CompleteStage();

                // else: let the held element drain via OnPull's IsClosed(In) check once it is pushed.
            }

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnDownstreamFinish(Exception cause) => InternalOnDownstreamFinish(cause);

            protected override void OnTimer(object timerKey)
            {
                if (RetryTimerKey.Equals(timerKey))
                {
                    RefreshCompletionFromContext();

                    if (_state == State.Completed)
                    {
                        CancelTimer(RetryTimerKey);
                        CancelTimer(TimeoutTimerKey);

                        if (_pendingMessage is { } held && IsAvailable(_stage.Out))
                        {
                            _pendingMessage = null;
                            Push(_stage.Out, held);
                        }

                        return;
                    }

                    TryInjectReq();
                    return;
                }

                if (TimeoutTimerKey.Equals(timerKey))
                {
                    if (_state != State.Completed)
                    {
                        FailStage(new HandshakeTimeoutException(
                            $"Handshake with remote address [{_stage.Context.RemoteAddress}] timed out after {_stage.HandshakeTimeout}."));
                    }
                }
            }

            private void RefreshCompletionFromContext()
            {
                if (_state == State.Completed)
                    return;

                var state = _stage.Context.AssociationState;

                if (state.UniqueRemoteAddress is not { } remote ||
                    !Equals(remote.Address, _stage.Context.RemoteAddress))
                    return;

                // If we are not the control stream, we cannot continue until the handshake is
                // done. The generation counter below also advances on the peer's own inbound Req,
                // so without this check an ordinary stream would complete on a Req retry that
                // lands while we wait for our Rsp. That is issue #8496 again.
                if (!_stage.IsControlStream && !state.OutboundHandshakeCompleted)
                    return;

                // A materialization that reached ReqInProgress (either its own first-ever
                // handshake, or a design.md group 9 restart via ForceReqOnStart) must observe the
                // HANDSHAKE GENERATION advance PAST this Logic instance's own baseline -- not
                // merely "AssociationState currently shows some peer for this address" -- proving
                // a Req/Rsp round-trip was actually processed AFTER this materialization started.
                // Without this, a restarted stream would trust STALE state left over from a peer
                // that has since restarted under a new uid and start flowing traffic to it before
                // it has ever actually handshaked THIS connection (design.md group 9's reconnect
                // correctness suite is what surfaced this -- see ForceReqOnStart's remarks).
                if (_stage.Context.HandshakeGeneration <= _handshakeGenerationBaseline)
                    return;

                _state = State.Completed;
                CancelTimer(RetryTimerKey);
                CancelTimer(TimeoutTimerKey);
                UnsubscribeHandshakeStateChanged();
                _lastInject = _timeProvider.Now;
            }

            private void TryInjectReq()
            {
                if (_state == State.Completed)
                    return;

                var now = _timeProvider.Now;
                var due = _lastInject is not { } lastInject || now - lastInject >= _stage.RetryInterval;
                if (!due)
                    return;

                if (!_stage.IsControlStream)
                {
                    // Side channel: never competes for this stream's own Out demand, so no
                    // IsAvailable(Out) guard is needed here.
                    _lastInject = now;
                    _stage.Context.SendControl(BuildReqMessage());
                    return;
                }

                if (!IsAvailable(_stage.Out))
                    return;

                _lastInject = now;
                Push(_stage.Out, BuildReqEnvelope());
            }

            private bool ShouldReinjectForLiveness()
            {
                var now = _timeProvider.Now;
                return _lastInject is not { } lastInject || now - lastInject >= _stage.InjectHandshakeInterval;
            }

            private HandshakeReq BuildReqMessage() => new(_stage.Context.LocalAddress, _stage.Context.RemoteAddress);

            private IOutboundEnvelope BuildReqEnvelope() => new OutboundEnvelope(BuildReqMessage(), null, null);
        }
    }
}
