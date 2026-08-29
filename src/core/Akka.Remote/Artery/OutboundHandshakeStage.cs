//-----------------------------------------------------------------------
// <copyright file="OutboundHandshakeStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
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
    /// DETECTS completion -- see the notification-mechanism note below.</para>
    ///
    /// <para><b>Handshake-completion notification mechanism.</b> Completion is PUSHED to this
    /// stage, never polled for. On entering <c>ReqInProgress</c> the logic registers a
    /// <c>GetAsyncCallback</c> with the association
    /// (<see cref="IOutboundContext.SubscribeHandshakeStateChanged"/>); the inbound pipeline's
    /// <see cref="AssociationRegistry.CompleteHandshake"/> /
    /// <see cref="AssociationRegistry.CompleteOutboundHandshake"/> fires it, and the stage
    /// re-verifies the association snapshot against its own completion rule before releasing
    /// traffic. The stage still READS the CAS-published <see cref="Artery.AssociationState"/>
    /// snapshot (safe from any thread) at <c>OnPull</c>/<c>OnPush</c>/the retry tick, but those are
    /// opportunistic; none of them is the detection path.</para>
    ///
    /// <para><b>Why polling was not enough.</b> While the stage holds a user element it stops
    /// pulling, and it has already pushed nothing, so NOTHING re-enters the logic: no <c>OnPull</c>
    /// (downstream demand is still outstanding), no <c>OnPush</c>. The only remaining wake-up was
    /// the retry tick, which made the first message on a freshly-gated association wait up to a
    /// full <c>handshake-retry-interval</c> (1s by default) after the peer had already answered.
    /// On a cluster join that is the seed's Welcome to every peer -- measured at ~1.07s versus
    /// ~0.07s once the wake-up is push-based.</para>
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
    /// <para><b>Completion means "the PEER knows OUR uid" (issue #8496)</b> -- proven only by a
    /// <see cref="HandshakeRsp"/> answering a <see cref="HandshakeReq"/> of ours, not by
    /// <see cref="Artery.AssociationState.UniqueRemoteAddress"/> (which the INBOUND direction sets
    /// too). See <c>Logic.CanSkipOwnHandshake</c>.</para>
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
            bool forceReqOnStart = false)
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
            Shape = new FlowShape<IOutboundEnvelope, IOutboundEnvelope>(In, Out);
        }

        public IOutboundContext Context { get; }
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
            /// Non-null exactly while this logic is registered with the association for
            /// handshake-state notifications (i.e. from <see cref="PreStart"/>'s
            /// <c>ReqInProgress</c> branch until completion or <see cref="PostStop"/>).
            /// </summary>
            private Action? _handshakeStateChangedCallback;

            private State _state = State.Start;
            private IOutboundEnvelope? _pendingMessage;
            private DateTime _lastInject = DateTime.MinValue;

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
                var state = _stage.Context.AssociationState;

                if (!_stage.ForceReqOnStart && CanSkipOwnHandshake(state))
                {
                    _state = State.Completed;
                    _lastInject = DateTime.UtcNow;
                    return;
                }

                if (!_stage.ForceReqOnStart && !_stage.IsControlStream && !state.OutboundHandshakeCompleted &&
                    state.UniqueRemoteAddress is not null)
                {
                    // Issue #8496's ordering, named so it is recognizable in the field.
                    Log.Debug(
                        "Outbound Artery stream to [{0}]: the peer's uid is known from an INBOUND handshake only, " +
                        "so this stream sends its own HandshakeReq and holds traffic until the peer answers.",
                        _stage.Context.RemoteAddress);
                }

                _handshakeGenerationBaseline = _stage.Context.HandshakeGeneration;
                _state = State.ReqInProgress;
                ScheduleRepeatedly(RetryTimerKey, _stage.RetryInterval);
                ScheduleOnce(TimeoutTimerKey, _stage.HandshakeTimeout);

                // REGISTER, THEN RE-CHECK -- the ordering is load-bearing. A completion landing
                // between the snapshot read at the top of this method and this registration would
                // otherwise be missed entirely, and (once this stage is holding an element and has
                // stopped pulling) nothing would re-enter the logic until the retry tick. Doing the
                // re-check after registering means such a completion is either seen by the check or
                // delivered by the callback -- never neither.
                _handshakeStateChangedCallback = GetAsyncCallback(OnHandshakeStateChanged);
                _stage.Context.SubscribeHandshakeStateChanged(this, _handshakeStateChangedCallback);
                RefreshCompletionFromContext();
            }

            public override void PostStop()
            {
                // Streams here are restarted (RestartFlow/ScheduleOutboundRestart) against an
                // association that long outlives them, so a leaked registration would accumulate.
                // Invoking an already-stopped stage's async callback is itself harmless -- the
                // interpreter drops async input for a completed logic -- so this is hygiene, not a
                // correctness race.
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
            /// The association told us its handshake state advanced. Re-verify against THIS
            /// stream's own completion rule (an ordinary stream waiting for its Rsp is not
            /// satisfied by the peer's inbound Req, nor by a completion belonging to a newer
            /// incarnation) and, if we are now complete, release exactly the way the retry-tick
            /// path used to.
            /// </summary>
            private void OnHandshakeStateChanged()
            {
                RefreshCompletionFromContext();

                if (_state != State.Completed)
                    return;

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
            /// May this materialization skip straight to <c>Completed</c> because another stream on
            /// the same association already did the work?
            ///
            /// <para>
            /// For an ORDINARY/LARGE/lane stream, only when the peer has answered a
            /// <see cref="HandshakeReq"/> of OURS
            /// (<see cref="Artery.AssociationState.OutboundHandshakeCompleted"/>). A known
            /// <see cref="Artery.AssociationState.UniqueRemoteAddress"/> does not qualify -- the
            /// peer's own inbound Req sets that, and it says nothing about whether the peer has
            /// registered our uid, so trusting it let our first user message race our
            /// <see cref="HandshakeRsp"/> into the peer's unknown-origin drop (issue #8496).
            /// </para>
            ///
            /// <para>
            /// The CONTROL stream keeps the older, weaker rule. That is safe -- the receiver's
            /// unknown-origin gate only ever drops ORDINARY envelopes; every control envelope
            /// (handshake, heartbeat, quarantine notice, system messages and their Ack/Nack) is
            /// dispatched whether or not the origin uid is registered. And it is REQUIRED: the
            /// <see cref="HandshakeRsp"/> we owe a peer is enqueued on that very control queue, so
            /// if the control stream held its traffic until the peer answered OUR Req, two systems
            /// that dial each other at the same instant would each sit holding the Rsp the other is
            /// waiting for, and neither handshake could ever complete.
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
                            _lastInject = DateTime.UtcNow;
                            Push(_stage.Out, BuildReqEnvelope());
                        }

                        return;
                    }

                    // Non-control stream: the Req travels via the control side channel and never
                    // occupies this stream's Out slot, so the user element can flow through
                    // immediately below -- no need to hold it.
                    _lastInject = DateTime.UtcNow;
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

                // Same control/ordinary split as CanSkipOwnHandshake, and load-bearing here too:
                // the generation counter below is advanced by the peer's own inbound Req as well,
                // so without this an ordinary stream would complete on the peer's Req retry that
                // happens to land while we wait for our Rsp -- issue #8496 through the back door.
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
                _lastInject = DateTime.UtcNow;
            }

            private void TryInjectReq()
            {
                if (_state == State.Completed)
                    return;

                var now = DateTime.UtcNow;
                var due = _lastInject == DateTime.MinValue || now - _lastInject >= _stage.RetryInterval;
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
                var now = DateTime.UtcNow;
                return _lastInject == DateTime.MinValue || now - _lastInject >= _stage.InjectHandshakeInterval;
            }

            private HandshakeReq BuildReqMessage() => new(_stage.Context.LocalAddress, _stage.Context.RemoteAddress);

            private IOutboundEnvelope BuildReqEnvelope() => new OutboundEnvelope(BuildReqMessage(), null, null);
        }
    }
}
