//-----------------------------------------------------------------------
// <copyright file="ArteryHeartbeatStage.cs" company="Akka.NET Project">
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
    /// Liveness heartbeat for the Artery control stream (design.md task group 6, "Control
    /// Stream" -- tasks 6.4/6.6). Inserted ONLY into the control outbound pipeline, UPSTREAM of
    /// <see cref="OutboundHandshakeStage"/> (see <c>ArteryRemoting.MaterializeOutboundStream</c>):
    /// a pass-through <c>GraphStage&lt;FlowShape&lt;IOutboundEnvelope, IOutboundEnvelope&gt;&gt;</c>
    /// that injects an <see cref="ArteryHeartbeat"/> control envelope whenever the stream has been
    /// idle (no element has flowed) for at least <see cref="Interval"/>.
    ///
    /// <para>
    /// <b>Why upstream of the handshake stage.</b> Placing this stage BEFORE
    /// <see cref="OutboundHandshakeStage"/> means a self-generated heartbeat is subject to the
    /// exact same "hold until handshake completes" gating as any other control-stream element --
    /// it cannot leak onto the wire ahead of (or instead of) the initial <see cref="HandshakeReq"/>.
    /// If this stage were downstream of the handshake stage instead, its injected elements would
    /// bypass that gate entirely.
    /// </para>
    ///
    /// <para>
    /// This is deliberately the same simplification style as
    /// <see cref="OutboundHandshakeStage"/>'s <c>inject-handshake-interval</c>: a repeating timer
    /// tracks "time since the last element flowed" (real traffic OR a previously injected
    /// heartbeat) and injects another heartbeat once that gap reaches <see cref="Interval"/>. It
    /// does not attempt to distinguish "genuinely idle" from "traffic paused, then resumed".
    /// </para>
    ///
    /// <para>
    /// <b>Never blocks real control traffic, never piles up.</b> A heartbeat is only pushed
    /// downstream when <c>Out</c> already has demand available; otherwise it is queued as
    /// <c>_pendingHeartbeat</c> and emitted at the very next <c>OnPull</c>, ahead of pulling more
    /// upstream input. At most one heartbeat is ever queued -- a timer tick that fires while one
    /// is already pending is a no-op (heartbeats are best-effort liveness signals, not a reliable
    /// channel; see design.md's "Ack/Nack best-effort" invariant for the analogous system-message
    /// case).
    /// </para>
    /// </summary>
    internal sealed class ArteryHeartbeatStage : GraphStage<FlowShape<IOutboundEnvelope, IOutboundEnvelope>>
    {
        private const string HeartbeatTimerKey = "ArteryHeartbeat-Tick";

        /// <summary>
        /// Initializes a new instance of the <see cref="ArteryHeartbeatStage"/> class.
        /// </summary>
        /// <param name="interval">How long the stream must be idle before a heartbeat is injected.</param>
        public ArteryHeartbeatStage(TimeSpan interval)
        {
            if (interval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(interval), interval, "must be positive.");

            Interval = interval;
            Shape = new FlowShape<IOutboundEnvelope, IOutboundEnvelope>(In, Out);
        }

        public TimeSpan Interval { get; }

        public Inlet<IOutboundEnvelope> In { get; } = new("ArteryHeartbeat.in");
        public Outlet<IOutboundEnvelope> Out { get; } = new("ArteryHeartbeat.out");

        public override FlowShape<IOutboundEnvelope, IOutboundEnvelope> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        private sealed class Logic : TimerGraphStageLogic, IInHandler, IOutHandler
        {
            private readonly ArteryHeartbeatStage _stage;
            private DateTime _lastActivity = DateTime.UtcNow;
            private IOutboundEnvelope? _pendingHeartbeat;

            public Logic(ArteryHeartbeatStage stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public override void PreStart() => ScheduleRepeatedly(HeartbeatTimerKey, _stage.Interval);

            public void OnPush()
            {
                _lastActivity = DateTime.UtcNow;
                Push(_stage.Out, Grab(_stage.In));
            }

            public void OnPull()
            {
                if (_pendingHeartbeat is { } heartbeat)
                {
                    _pendingHeartbeat = null;
                    _lastActivity = DateTime.UtcNow;
                    Push(_stage.Out, heartbeat);
                    return;
                }

                if (!HasBeenPulled(_stage.In) && !IsClosed(_stage.In))
                    Pull(_stage.In);
            }

            public void OnUpstreamFinish()
            {
                if (_pendingHeartbeat is null)
                    CompleteStage();

                // else: let the queued heartbeat drain via OnPull first.
            }

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnDownstreamFinish(Exception cause) => InternalOnDownstreamFinish(cause);

            protected override void OnTimer(object timerKey)
            {
                if (!HeartbeatTimerKey.Equals(timerKey))
                    return;

                if (_pendingHeartbeat is not null)
                    return; // one already queued and not yet drained -- never pile up more.

                var now = DateTime.UtcNow;
                if (now - _lastActivity < _stage.Interval)
                    return;

                _lastActivity = now;
                var heartbeat = new OutboundEnvelope(new ArteryHeartbeat(), null, null);

                if (IsAvailable(_stage.Out))
                    Push(_stage.Out, heartbeat);
                else
                    _pendingHeartbeat = heartbeat;
            }
        }
    }
}
