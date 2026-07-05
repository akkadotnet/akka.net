//-----------------------------------------------------------------------
// <copyright file="OutboundHandshakeStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using Akka.Streams;
using Akka.Streams.Stage;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Outbound half of the Artery handshake, faithful to
    /// <c>openspec/changes/artery-tcp-remoting/design.md</c>
    /// ("Handshake + association/UID (gate G2)"). A <c>GraphStage&lt;FlowShape&lt;object, object&gt;&gt;</c>
    /// — see the element-type note on <see cref="IInboundContext"/> for why <c>object</c> rather
    /// than a dedicated envelope type.
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
    /// never completes (the association is expected to retry the outbound stream).</para>
    ///
    /// <para><b>Handshake-completion notification mechanism (chosen &amp; documented, per the
    /// task).</b> This stage does NOT use a <c>Task</c>/promise or a cross-stage async callback.
    /// It polls <see cref="IOutboundContext.AssociationState"/> — a lock-free, CAS-published
    /// snapshot (see <see cref="Artery.AssociationState"/>) that is safe to read from any thread —
    /// at every event the stage's logic already processes: <c>OnPull</c>, <c>OnPush</c>, and the
    /// retry timer tick. This works because the SAME <see cref="AssociationRegistry"/> backs both
    /// this stage's <see cref="IOutboundContext"/> and the peer-facing
    /// <see cref="AssociationRegistryInboundContext"/> that actually calls
    /// <see cref="AssociationRegistry.CompleteHandshake"/> when a <see cref="HandshakeRsp"/>
    /// arrives on the inbound pipeline for the return direction — the state IS the coordination
    /// primitive, so no separate wakeup channel is needed for correctness. The retry timer (not
    /// just event-driven polling) bounds the worst-case detection latency to one
    /// <c>handshake-retry-interval</c> even if the stream is otherwise idle when completion
    /// happens.</para>
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
    /// </summary>
    internal sealed class OutboundHandshakeStage : GraphStage<FlowShape<object, object>>
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
            TimeSpan injectHandshakeInterval)
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
            Shape = new FlowShape<object, object>(In, Out);
        }

        public IOutboundContext Context { get; }
        public TimeSpan RetryInterval { get; }
        public TimeSpan HandshakeTimeout { get; }
        public TimeSpan InjectHandshakeInterval { get; }

        public Inlet<object> In { get; } = new("OutboundHandshake.in");
        public Outlet<object> Out { get; } = new("OutboundHandshake.out");

        public override FlowShape<object, object> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        private sealed class Logic : TimerGraphStageLogic, IInHandler, IOutHandler
        {
            private readonly OutboundHandshakeStage _stage;
            private State _state = State.Start;
            private object? _pendingMessage;
            private DateTime _lastInject = DateTime.MinValue;

            public Logic(OutboundHandshakeStage stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public override void PreStart()
            {
                if (_stage.Context.AssociationState.UniqueRemoteAddress is { } already &&
                    Equals(already.Address, _stage.Context.RemoteAddress))
                {
                    _state = State.Completed;
                    _lastInject = DateTime.UtcNow;
                    return;
                }

                _state = State.ReqInProgress;
                ScheduleRepeatedly(RetryTimerKey, _stage.RetryInterval);
                ScheduleOnce(TimeoutTimerKey, _stage.HandshakeTimeout);
            }

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
                    _pendingMessage = elem;

                    if (IsAvailable(_stage.Out))
                    {
                        _lastInject = DateTime.UtcNow;
                        Push(_stage.Out, BuildReq());
                    }

                    return;
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

                if (_stage.Context.AssociationState.UniqueRemoteAddress is not { } remote ||
                    !Equals(remote.Address, _stage.Context.RemoteAddress))
                    return;

                _state = State.Completed;
                CancelTimer(RetryTimerKey);
                CancelTimer(TimeoutTimerKey);
                _lastInject = DateTime.UtcNow;
            }

            private void TryInjectReq()
            {
                if (_state == State.Completed)
                    return;

                var now = DateTime.UtcNow;
                var due = _lastInject == DateTime.MinValue || now - _lastInject >= _stage.RetryInterval;
                if (!due || !IsAvailable(_stage.Out))
                    return;

                _lastInject = now;
                Push(_stage.Out, BuildReq());
            }

            private bool ShouldReinjectForLiveness()
            {
                var now = DateTime.UtcNow;
                return _lastInject == DateTime.MinValue || now - _lastInject >= _stage.InjectHandshakeInterval;
            }

            private HandshakeReq BuildReq() => new(_stage.Context.LocalAddress, _stage.Context.RemoteAddress);
        }
    }
}
