//-----------------------------------------------------------------------
// <copyright file="InboundQuarantineCheckStage.cs" company="Akka.NET Project">
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
    /// Inbound counterpart of the quarantine gate <see cref="ArteryRemoting.Send"/> already applies
    /// on the OUTBOUND path. Port of Pekko's <c>InboundQuarantineCheck</c>
    /// (<c>remote/.../artery/InboundQuarantineCheck.scala</c>) -- positioned exactly where Pekko
    /// places it in both its <c>inboundSink</c> and <c>inboundControlSink</c>
    /// (<c>ArteryTransport.scala:919/939</c>): AFTER <see cref="InboundHandshakeStage"/>, BEFORE
    /// <see cref="SystemMessageAckerStage"/>/dispatch. This codebase has only ONE physical inbound
    /// sink (ordinary, control, and large connections all feed the same shape -- see
    /// <c>ArteryRemoting.HandleIncomingConnection</c>'s remarks), so composing this stage once,
    /// between those same two stages, covers both of Pekko's sink insertion points.
    ///
    /// <para>
    /// <b>Logic (mirrors Pekko's <c>onPush</c> exactly).</b> For every envelope: resolve whether
    /// <see cref="IInboundEnvelope.OriginUid"/> is quarantined for its association
    /// (<see cref="IInboundContext.IsQuarantined"/>, the SAME shared-registry lookup
    /// <see cref="IInboundContext.IsKnownOrigin"/> uses -- <see langword="false"/> for an unknown
    /// uid, matching Pekko's <c>OptionVal.None</c> pass-through). If quarantined: log at debug and
    /// DROP unconditionally -- including a <see cref="ArteryQuarantined"/> notice or a heartbeat
    /// from that uid, exactly like Pekko drops everything once quarantined -- then, UNLESS the
    /// dropped message is itself an <see cref="ArteryQuarantined"/> notice or a heartbeat-class
    /// message (Pekko's <c>isHeartbeat</c>: avoids starting a reply storm for routine liveness
    /// traffic and avoids notification ping-pong), reactively re-notify the origin with a fresh
    /// <see cref="ArteryQuarantined"/> over the control channel -- the SAME
    /// <see cref="ArteryRemoting.EnqueueControl"/> path <see cref="ArteryRemoting.Quarantine"/>'s
    /// own one-shot proactive notice uses (<see cref="IInboundContext.SendControl"/> ->
    /// <c>ArteryRemoting.SendControlToAddress</c> -> <c>EnqueueControl</c>), which already carries
    /// the shutdown/overflow guards that path needs. If NOT quarantined (or the origin is unknown):
    /// pass through unchanged.
    /// </para>
    ///
    /// <para>
    /// <b>Pekko keeps its own proactive notice too (verified against <c>Association.quarantine</c>,
    /// <c>Association.scala:586-589</c>: <c>if (!harmless) sendControl(Quarantined(...))</c> inside
    /// the SAME transaction that flips the association's quarantined bit) -- so this port does
    /// likewise: <see cref="ArteryRemoting.Quarantine"/>'s existing one-shot send is UNCHANGED, and
    /// this stage adds the reactive, per-dropped-message counterpart Pekko's inbound stage
    /// contributes on top of it. Two notices are strictly more (not less) faithful than one: the
    /// proactive send may race the peer's own outbound materialization/handshake and be lost, and
    /// only the reactive path repeats it for as long as the peer keeps trying to talk to us.</b>
    /// </para>
    /// </summary>
    internal sealed class InboundQuarantineCheckStage : GraphStage<FlowShape<IInboundEnvelope, IInboundEnvelope>>
    {
        public InboundQuarantineCheckStage(IInboundContext context)
        {
            Context = context;
            Shape = new FlowShape<IInboundEnvelope, IInboundEnvelope>(In, Out);
        }

        public IInboundContext Context { get; }

        public Inlet<IInboundEnvelope> In { get; } = new("InboundQuarantineCheck.in");
        public Outlet<IInboundEnvelope> Out { get; } = new("InboundQuarantineCheck.out");

        public override FlowShape<IInboundEnvelope, IInboundEnvelope> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        private sealed class Logic : GraphStageLogic, IInHandler, IOutHandler
        {
            private readonly InboundQuarantineCheckStage _stage;

            public Logic(InboundQuarantineCheckStage stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public void OnPush()
            {
                var envelope = Grab(_stage.In);

                if (!_stage.Context.IsQuarantined(envelope.OriginUid))
                {
                    Push(_stage.Out, envelope);
                    return;
                }

                if (Log.IsDebugEnabled)
                    Log.Debug(
                        "Dropping message [{0}] from [{1}#{2}] because the system is quarantined",
                        envelope.Message.GetType(), _stage.Context.TryResolveOriginAddress(envelope.OriginUid), envelope.OriginUid);

                // Avoid starting an outbound stream / a notification ping-pong for a routine
                // heartbeat or for the peer's own Quarantined notice -- mirrors Pekko's
                // isHeartbeat/Quarantined carve-out (applies ONLY to whether we react, never to
                // whether we drop: every message from a quarantined uid is dropped either way,
                // just below).
                if (envelope.Message is not ArteryQuarantined && !IsHeartbeat(envelope.Message))
                {
                    var origin = _stage.Context.TryResolveOriginAddress(envelope.OriginUid);
                    if (origin is not null)
                        _stage.Context.SendControl(
                            origin,
                            new ArteryQuarantined(_stage.Context.LocalAddress, envelope.OriginUid));
                }

                Pull(_stage.In); // drop message
            }

            /// <summary>
            /// Pekko's <c>isHeartbeat</c>: matches its own control-channel liveness ping/pong
            /// (<see cref="ArteryHeartbeat"/>/<see cref="ArteryHeartbeatRsp"/> -- the artery analog
            /// of Pekko's <c>RemoteWatcher.ArteryHeartbeat</c>/<c>ArteryHeartbeatRsp</c>, both
            /// <c>HeartbeatMessage</c>) plus <see cref="RemoteWatcher"/>'s own ordinary-stream
            /// heartbeat (<see cref="IPriorityMessage"/> -- the marker <see cref="RemoteWatcher.Heartbeat"/>/
            /// <see cref="RemoteWatcher.HeartbeatRsp"/> implement, this codebase's analog of Pekko's
            /// shared <c>HeartbeatMessage</c> trait), including when it arrives wrapped in an
            /// <see cref="ActorSelectionMessage"/> (Pekko's <c>ActorSelectionMessage(_: HeartbeatMessage, _, _)</c>
            /// -- <see cref="RemoteWatcher"/> sends its heartbeat via <c>Context.ActorSelection(...).Tell</c>).
            /// </summary>
            private static bool IsHeartbeat(object message) => message switch
            {
                ArteryHeartbeat => true,
                ArteryHeartbeatRsp => true,
                IPriorityMessage => true,
                ActorSelectionMessage { Message: IPriorityMessage } => true,
                _ => false
            };

            public void OnPull() => Pull(_stage.In);

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnDownstreamFinish(Exception cause) => InternalOnDownstreamFinish(cause);
        }
    }
}
