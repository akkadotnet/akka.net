//-----------------------------------------------------------------------
// <copyright file="SystemMessageDeliveryStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Association-owned, stream-restart-surviving state for the OUTBOUND half of reliable
    /// system-message delivery (design.md gate G3's <see cref="SystemMessageDeliveryStage"/>,
    /// extended by design.md group 9, "Association outbound-stream lifecycle: reconnect",
    /// invariant 3: "no unacked system message may be lost across a restart").
    ///
    /// <para>
    /// <b>Why this exists (the bug it fixes).</b> Before group 9, <c>SystemMessageDeliveryStage.Logic</c>
    /// held its unacknowledged buffer, seqNo counter, and observed incarnation as PER-MATERIALIZATION
    /// instance fields. Group 9 makes the control stream's outbound connection restart after a
    /// failure -- but a stream restart tears down the old <c>GraphStageLogic</c> and creates a
    /// brand-new one, which would have started from an EMPTY buffer/seqNo 1, silently losing every
    /// unacknowledged <see cref="SystemMessageEnvelope"/> in flight (Watch/Unwatch/DeathWatchNotification/
    /// Terminate) -- exactly the invariant group 9 requires never happen. Moving this state onto the
    /// <see cref="Artery.Association"/> (looked up via the SAME <see cref="AssociationRegistry"/> every
    /// materialization's <see cref="IOutboundContext"/> already closes over) means a fresh
    /// materialization ATTACHES to the same buffer instead of starting a new one -- see
    /// <see cref="Artery.Association.SystemMessageDeliveryState"/> and
    /// <c>ArteryRemoting.MaterializeOutboundStream</c> (which passes it into this stage's constructor).
    /// </para>
    ///
    /// <para>
    /// <b>Concurrency.</b> No locking: the materialize-once gate (<see cref="MaterializeOnceGate"/>)
    /// guarantees at most one live <c>SystemMessageDeliveryStage.Logic</c> instance is ever running
    /// for a given association's control stream at a time, and a restart only schedules the NEXT
    /// materialization once the previous one's completion has already been observed -- so this state
    /// is handed off sequentially between single-threaded owners, never touched concurrently.
    /// </para>
    /// </summary>
    internal sealed class SystemMessageDeliveryState
    {
        /// <summary>
        /// Unacknowledged envelopes, in ascending seq order (FIFO -- Ack/Nack pop a PREFIX).
        /// Survives stream restart -- see the type-level remarks.
        /// </summary>
        public Queue<(long Seq, IOutboundEnvelope Envelope, DateTime SentAt)> Buffer { get; } = new();

        /// <summary>
        /// The next per-incarnation monotonic sequence number to assign. Starts at 1 (the default
        /// <see langword="long"/> value 0 is never a valid assigned seqNo).
        /// </summary>
        public long NextSeq { get; set; } = 1;

        /// <summary>
        /// The <see cref="Artery.AssociationState.Incarnation"/> this state was last observed/reset
        /// against. 0 (the default) is a deliberate "never initialized" sentinel -- real incarnations
        /// start at 1 (<see cref="Artery.AssociationState.Create"/>) -- so the FIRST-ever materialization
        /// can distinguish "brand new association, nothing to refresh" from "a restart that needs to
        /// check whether a new incarnation completed while this stream was down".
        /// </summary>
        public int CurrentIncarnation { get; set; }
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// Outbound half of reliable system-message delivery (design.md gate G3, "Reliable
    /// system-message delivery"). A <c>GraphStage&lt;FlowShape&lt;IOutboundEnvelope, IOutboundEnvelope&gt;&gt;</c>
    /// materialized ONLY on the CONTROL stream (see <c>ArteryRemoting.MaterializeOutboundStream</c>),
    /// positioned BETWEEN <see cref="ArteryHeartbeatStage"/> and <see cref="OutboundHandshakeStage"/>
    /// -- upstream of the handshake stage so a freshly-wrapped <see cref="SystemMessageEnvelope"/> is
    /// gated by handshake completion exactly like every other control-stream element (it is held
    /// behind <see cref="OutboundHandshakeStage"/>'s <c>pendingMessage</c> until the association
    /// completes, never dropped).
    ///
    /// <para>
    /// <b>What it does to each element:</b>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="ISystemMessage"/> payload: assigns the next per-incarnation monotonic sequence
    /// number (starting at 1), wraps it as <see cref="SystemMessageEnvelope"/>
    /// (<c>ackReplyTo</c> = this system's own <see cref="IOutboundContext.LocalAddress"/>), buffers
    /// it (bounded, <c>system-message-buffer-size</c>) for possible resend, and emits the wrapped
    /// envelope downstream.
    /// </description></item>
    /// <item><description>
    /// <see cref="ClearSystemMessageDelivery"/>: intercepted and CONSUMED -- never forwarded to
    /// <c>Out</c>/the encoder. See that type's doc comments for why this is local-only (documented
    /// simplification vs. Pekko).
    /// </description></item>
    /// <item><description>
    /// Anything else (heartbeat, handshake-injected-via-side-channel n/a here, <see cref="Ack"/>/
    /// <see cref="Nack"/> replies generated by this system's OWN <see cref="SystemMessageAckerStage"/>
    /// replying to the peer, quarantine notices): passed through unchanged.
    /// </description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Ack/Nack consumption (async, cross-thread).</b> <see cref="Ack"/>/<see cref="Nack"/>
    /// REPLIES from the peer arrive on the INBOUND pipeline (a different materialized stream, a
    /// different execution context) and are broadcast via <c>ArteryRemoting</c>'s generic
    /// <see cref="IControlMessageSubscriber"/> mechanism (the same one heartbeat/quarantine use).
    /// This stage's logic subscribes itself (<see cref="IOutboundContext.SubscribeControl"/>) and
    /// bridges into its own single-threaded execution via <c>GetAsyncCallback</c> -- the standard,
    /// safe re-entry mechanism for a <c>GraphStageLogic</c> to be notified from another thread.
    /// </para>
    ///
    /// <para>
    /// <b>INVARIANT 3 -- the #6414 stale-ack guard (MANDATORY).</b> An incoming Ack/Nack is
    /// processed ONLY when its originating uid (decoded from the wire envelope's own header, passed
    /// alongside the message by <c>IControlMessageSubscriber.ControlMessageReceived</c>) equals this
    /// association's CURRENT <see cref="Artery.AssociationState.UniqueRemoteAddress"/> uid. A
    /// mismatch is EITHER a broadcast notification for a totally different association (globally
    /// shared subscription list) OR a late reply from a PRIOR incarnation of THIS association --
    /// either way it must never mutate the unacknowledged buffer or trigger a quarantine. This is the
    /// direct analogue of classic Akka.NET's #6414 fix in <c>AckedSendBuffer&lt;T&gt;.Acknowledge</c>
    /// (there guarded by a <c>CumulativeAck &gt; MaxSeq</c> seq comparison; here guarded structurally
    /// by uid, which Artery's simpler protocol makes possible).
    /// </para>
    ///
    /// <para>
    /// <b>Give-up (buffer overflow OR resend-timeout) -&gt; quarantine, never a silent drop
    /// (invariant 4).</b> If the unacknowledged buffer is full when a new system message arrives, OR
    /// if the resend timer observes the OLDEST unacknowledged entry has been waiting longer than
    /// <c>give-up-system-message-after</c>, this stage resets its own delivery state (seqNo back to
    /// 1, buffer emptied) AND calls <see cref="IOutboundContext.Quarantine"/>. Once quarantined,
    /// <c>ArteryRemoting.Send</c> stops routing FURTHER system messages for this uid at all (see that
    /// method's quarantine gate) -- so the immediate local reset is safe: nothing more will be sent
    /// under the just-given-up incarnation.
    /// </para>
    ///
    /// <para>
    /// <b>Resend-batch backpressure (documented simplification).</b> The resend timer only starts a
    /// NEW resend cycle when the previous one has fully drained downstream (<c>_outQueue</c> empty)
    /// -- this bounds queued-for-emission memory to at most one buffer's worth at a time, at the cost
    /// of occasionally skipping a tick under sustained backpressure (the NEXT tick will catch up).
    /// </para>
    /// </summary>
    internal sealed class SystemMessageDeliveryStage : GraphStage<FlowShape<IOutboundEnvelope, IOutboundEnvelope>>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SystemMessageDeliveryStage"/> class.
        /// </summary>
        /// <param name="context">The outbound association seam (local/remote address, association state, control send/subscribe, quarantine).</param>
        /// <param name="state">
        /// The Association-owned, stream-restart-surviving unacked-buffer/seqNo/incarnation state
        /// (design.md group 9 invariant 3) -- see <see cref="SystemMessageDeliveryState"/>.
        /// </param>
        /// <param name="bufferCapacity">Maximum unacknowledged <see cref="SystemMessageEnvelope"/>s buffered for resend (<c>system-message-buffer-size</c>).</param>
        /// <param name="resendInterval">How often the whole unacknowledged window is resent (<c>system-message-resend-interval</c>).</param>
        /// <param name="giveUpAfter">How long the OLDEST unacknowledged entry may wait before giving up (<c>give-up-system-message-after</c>).</param>
        public SystemMessageDeliveryStage(IOutboundContext context, SystemMessageDeliveryState state, int bufferCapacity, TimeSpan resendInterval, TimeSpan giveUpAfter)
        {
            if (bufferCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(bufferCapacity), bufferCapacity, "must be positive.");
            if (resendInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(resendInterval), resendInterval, "must be positive.");
            if (giveUpAfter <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(giveUpAfter), giveUpAfter, "must be positive.");

            Context = context;
            State = state ?? throw new ArgumentNullException(nameof(state));
            BufferCapacity = bufferCapacity;
            ResendInterval = resendInterval;
            GiveUpAfter = giveUpAfter;
            Shape = new FlowShape<IOutboundEnvelope, IOutboundEnvelope>(In, Out);
        }

        public IOutboundContext Context { get; }

        /// <summary>The restart-surviving unacked-buffer/seqNo/incarnation state this materialization attaches to -- see <see cref="SystemMessageDeliveryState"/>.</summary>
        public SystemMessageDeliveryState State { get; }

        public int BufferCapacity { get; }
        public TimeSpan ResendInterval { get; }
        public TimeSpan GiveUpAfter { get; }

        public Inlet<IOutboundEnvelope> In { get; } = new("SystemMessageDelivery.in");
        public Outlet<IOutboundEnvelope> Out { get; } = new("SystemMessageDelivery.out");

        public override FlowShape<IOutboundEnvelope, IOutboundEnvelope> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        private sealed class Logic : TimerGraphStageLogic, IInHandler, IOutHandler, IControlMessageSubscriber
        {
            private const string ResendTimerKey = "SystemMessageDelivery-Resend";

            private readonly SystemMessageDeliveryStage _stage;

            /// <summary>
            /// The unacknowledged buffer/seqNo/incarnation state this materialization attaches to --
            /// Association-owned, NOT per-materialization (design.md group 9 invariant 3). See
            /// <see cref="SystemMessageDeliveryState"/>'s type-level remarks for why.
            /// </summary>
            private SystemMessageDeliveryState State => _stage.State;

            /// <summary>Elements queued for emission downstream (pass-through + freshly-wrapped + resend batches). Per-materialization -- does not need to survive a restart, see PreStart's eager-requeue remarks.</summary>
            private readonly Queue<IOutboundEnvelope> _outQueue = new();

            private Action<(long OriginUid, object Message)>? _controlCallback;

            public Logic(SystemMessageDeliveryStage stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public override void PreStart()
            {
                if (State.CurrentIncarnation == 0)
                    // Brand-new state (never initialized) -- the FIRST-ever materialization for this
                    // association's control stream. Not a restart, so there is nothing to "refresh":
                    // adopt the currently-observed incarnation directly.
                    State.CurrentIncarnation = _stage.Context.AssociationState.Incarnation;
                else
                    // A restart (this state has been observed before) -- a NEW incarnation may have
                    // completed its handshake while this stream was down; reset if so (same check
                    // RefreshIncarnation performs on every subsequent event).
                    RefreshIncarnation();

                _controlCallback = GetAsyncCallback<(long OriginUid, object Message)>(HandleControlMessage);
                _stage.Context.SubscribeControl(this);
                ScheduleRepeatedly(ResendTimerKey, _stage.ResendInterval);

                // Eagerly requeue any already-buffered unacked entries (design.md group 9 invariant 3:
                // "no unacked system message may be lost across a restart") -- rather than waiting up
                // to one full resend-interval for the timer to notice, get them flowing again
                // immediately. They are still held behind OutboundHandshakeStage until the FRESH
                // handshake completes, exactly like any other control-stream element -- so this is
                // safe even before the peer is reachable again.
                if (State.Buffer.Count > 0)
                    foreach (var entry in State.Buffer)
                        _outQueue.Enqueue(entry.Envelope);
            }

            public override void PostStop() => _stage.Context.UnsubscribeControl(this);

            /// <inheritdoc/>
            /// <remarks>
            /// Called from the INBOUND pipeline's execution context (a different thread) -- must not
            /// touch any of this logic's mutable state directly. Bridges via the async callback
            /// obtained in <see cref="PreStart"/>.
            /// </remarks>
            void IControlMessageSubscriber.ControlMessageReceived(long originUid, object message) =>
                _controlCallback?.Invoke((originUid, message));

            private void HandleControlMessage((long OriginUid, object Message) evt)
            {
                // INVARIANT 3 / #6414 stale-ack guard -- see the type-level remarks. A mismatch is
                // silently ignored: it is either irrelevant (a different association's traffic,
                // broadcast globally) or a stale reply from a superseded incarnation of THIS one.
                var currentUid = _stage.Context.AssociationState.UniqueRemoteAddress?.Uid;

                switch (evt.Message)
                {
                    case Ack ack when evt.OriginUid == currentUid:
                        ProcessAck(ack.SeqNo);
                        break;

                    case Nack nack when evt.OriginUid == currentUid:
                        ProcessNack(nack.SeqNo);
                        break;
                }

                TryDeliver();
            }

            private void ProcessAck(long seqNo)
            {
                while (State.Buffer.Count > 0 && State.Buffer.Peek().Seq <= seqNo)
                    State.Buffer.Dequeue();
            }

            private void ProcessNack(long seqNo)
            {
                while (State.Buffer.Count > 0 && State.Buffer.Peek().Seq <= seqNo)
                    State.Buffer.Dequeue();

                // Immediate tail resend -- do not wait for the next resend-timer tick. Skipped if a
                // batch is already queued (bounds memory; see the type-level backpressure remarks).
                if (State.Buffer.Count > 0 && _outQueue.Count == 0)
                    foreach (var entry in State.Buffer)
                        _outQueue.Enqueue(entry.Envelope);
            }

            public void OnPush()
            {
                var elem = Grab(_stage.In);
                RefreshIncarnation();

                switch (elem.Message)
                {
                    case ClearSystemMessageDelivery clear:
                        if (clear.Incarnation >= State.CurrentIncarnation)
                            ResetDeliveryState(clear.Incarnation);
                        // Local-only instruction -- never forwarded to Out/the encoder; see
                        // ClearSystemMessageDelivery's type-level remarks.
                        break;

                    case ISystemMessage systemMessage:
                        EnqueueSystemMessage(systemMessage, elem);
                        break;

                    default:
                        _outQueue.Enqueue(elem);
                        break;
                }

                DeliverOrPull();
            }

            private void EnqueueSystemMessage(ISystemMessage systemMessage, IOutboundEnvelope elem)
            {
                if (State.Buffer.Count >= _stage.BufferCapacity)
                {
                    GiveUp($"unacknowledged system-message buffer overflow (system-message-buffer-size = {_stage.BufferCapacity})");
                    Log.Warning(
                        "Dropping system message of type [{0}] to [{1}]: buffer overflow triggered give-up/quarantine.",
                        systemMessage.GetType(), _stage.Context.RemoteAddress);
                    return;
                }

                var seq = State.NextSeq++;
                var envelope = new SystemMessageEnvelope(systemMessage, seq, _stage.Context.LocalAddress, elem.RecipientPath ?? string.Empty);
                var wrapped = new OutboundEnvelope(envelope, null, null);
                State.Buffer.Enqueue((seq, wrapped, DateTime.UtcNow));
                _outQueue.Enqueue(wrapped);
            }

            public void OnPull()
            {
                if (_outQueue.Count > 0)
                {
                    Push(_stage.Out, _outQueue.Dequeue());
                    return;
                }

                if (!IsClosed(_stage.In) && !HasBeenPulled(_stage.In))
                    Pull(_stage.In);
            }

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnDownstreamFinish(Exception cause) => InternalOnDownstreamFinish(cause);

            protected override void OnTimer(object timerKey)
            {
                if (!ResendTimerKey.Equals(timerKey))
                    return;

                RefreshIncarnation();

                if (State.Buffer.Count == 0)
                    return;

                var oldest = State.Buffer.Peek();
                if (DateTime.UtcNow - oldest.SentAt > _stage.GiveUpAfter)
                {
                    GiveUp($"give-up-system-message-after ({_stage.GiveUpAfter}) exceeded waiting for ack of seq [{oldest.Seq}]");
                    return;
                }

                // Only start a fresh resend cycle once the previous one has fully drained, and only
                // once the association is actually established (nothing useful to resend to a peer
                // whose uid isn't known yet -- OutboundHandshakeStage would hold it anyway, but no
                // sense growing _outQueue for it).
                if (_outQueue.Count == 0 && _stage.Context.AssociationState.UniqueRemoteAddress is not null)
                {
                    foreach (var entry in State.Buffer)
                        _outQueue.Enqueue(entry.Envelope);

                    DeliverOrPull();
                }
            }

            private void GiveUp(string reason)
            {
                Log.Warning(
                    "System-message delivery to [{0}] giving up: {1}. Quarantining the association.",
                    _stage.Context.RemoteAddress, reason);
                ResetDeliveryState(State.CurrentIncarnation);
                _stage.Context.Quarantine();
            }

            /// <summary>
            /// Resets seqNo (back to 1) and empties the unacknowledged buffer -- idempotent, called
            /// both automatically (a genuinely new incarnation observed via
            /// <see cref="RefreshIncarnation"/>) and explicitly (<see cref="ClearSystemMessageDelivery"/>
            /// / give-up). Deliberately does NOT clear <see cref="_outQueue"/> -- any already-queued
            /// pass-through/resend elements are harmless to let drain (the control stream keeps
            /// flowing even once quarantined; see design.md's control-pierces-quarantine semantics).
            /// Mutates the SHARED <see cref="State"/> (design.md group 9 invariant 3) -- this is why
            /// buffer/seqNo resets survive (or rather, correctly do NOT survive across an actual
            /// incarnation change) a stream restart the exact same way they did before that state
            /// moved off this per-materialization <c>Logic</c>.
            /// </summary>
            private void ResetDeliveryState(int incarnation)
            {
                State.Buffer.Clear();
                State.NextSeq = 1;
                State.CurrentIncarnation = incarnation;
            }

            /// <summary>
            /// Detects a remote restart (a genuinely NEW incarnation, i.e. a NEW peer uid completed
            /// a NEW handshake) purely by observing <see cref="Artery.AssociationState.Incarnation"/>
            /// advance -- design.md invariant 2 ("new incarnation -&gt; expected restarts at 1").
            /// Explicit quarantine of the CURRENT (unchanged) uid does NOT advance <c>Incarnation</c>
            /// by itself -- that case is handled by <see cref="ClearSystemMessageDelivery"/>/give-up
            /// instead (see their remarks).
            /// </summary>
            private void RefreshIncarnation()
            {
                var observed = _stage.Context.AssociationState.Incarnation;
                if (observed != State.CurrentIncarnation)
                    ResetDeliveryState(observed);
            }

            private void DeliverOrPull()
            {
                if (_outQueue.Count > 0)
                {
                    if (IsAvailable(_stage.Out))
                        Push(_stage.Out, _outQueue.Dequeue());
                    return;
                }

                if (!IsClosed(_stage.In) && !HasBeenPulled(_stage.In))
                    Pull(_stage.In);
            }

            /// <summary>
            /// Pushes ONE queued element if <c>Out</c> currently has demand -- used from the async
            /// control-message callback, which runs OUTSIDE the normal push/pull reactive dance.
            /// </summary>
            private void TryDeliver()
            {
                if (_outQueue.Count > 0 && IsAvailable(_stage.Out))
                    Push(_stage.Out, _outQueue.Dequeue());
            }
        }
    }
}
