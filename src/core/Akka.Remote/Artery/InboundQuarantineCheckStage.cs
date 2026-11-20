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
    /// This stage applies quarantine to the inbound path. <see cref="ArteryRemoting.Send"/> already
    /// applies quarantine to the outbound path.
    ///
    /// <para>The stage does this with each envelope:</para>
    /// <list type="number">
    /// <item><description>
    /// It asks <see cref="IInboundContext.IsQuarantined"/> if the origin uid is quarantined. An
    /// unknown uid is not quarantined.
    /// </description></item>
    /// <item><description>
    /// If the uid is not quarantined, the stage sends the envelope to the next stage.
    /// </description></item>
    /// <item><description>
    /// If the uid is quarantined, the stage writes a debug log entry and discards the envelope. It
    /// discards all envelopes from that uid, including heartbeats and quarantine notices.
    /// </description></item>
    /// <item><description>
    /// The stage then sends a new <see cref="ArteryQuarantined"/> notice to the origin.
    /// <see cref="ShouldNotifyOrigin"/> says when it sends this notice: never in reply to a notice
    /// from the peer, and at most once per second in reply to heartbeats.
    /// </description></item>
    /// </list>
    ///
    /// <para>
    /// The notice goes through <see cref="IInboundContext.SendControl"/>.
    /// <see cref="ArteryRemoting.Quarantine"/> uses the same path for its single notice. Thus the
    /// notice from this stage gets the same protection against shutdown and queue overflow.
    /// </para>
    ///
    /// <para>
    /// <see cref="ArteryRemoting.Quarantine"/> keeps its single notice, and this stage adds a
    /// repeated notice. The two notices together are more reliable than one notice. The single
    /// notice can be lost if it occurs during the handshake of the peer. This stage sends a new
    /// notice for as long as the peer continues to send messages.
    /// </para>
    ///
    /// <para>
    /// Position in the pipeline: after <see cref="InboundHandshakeStage"/> and before
    /// <see cref="SystemMessageAckerStage"/>. Ordinary, control and large connections all use one
    /// inbound sink in this codebase. Thus one instance of this stage is sufficient.
    /// </para>
    ///
    /// <para>
    /// There is one exception. If <c>inbound-lanes</c> is more than 1, lane-routed traffic on an
    /// ordinary connection does not go through the sink. Therefore
    /// <c>ArteryInboundProcessingStage.ProcessFrameLaneMode</c> does the same check in its own code.
    /// The two sites call <see cref="ShouldNotifyOrigin"/> so that they stay in agreement.
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

        /// <summary>
        /// Tells the caller if a discarded message must cause a new
        /// <see cref="ArteryQuarantined"/> notice to the origin. The caller has already found that
        /// the message comes from a quarantined uid.
        ///
        /// <list type="bullet">
        /// <item><description>
        /// An <see cref="ArteryQuarantined"/> notice from the peer never causes a notice. A reply
        /// to this notice would cause the two systems to send notices to each other continuously.
        /// </description></item>
        /// <item><description>
        /// A heartbeat causes a notice at most once per <see cref="HeartbeatNoticeInterval"/> per
        /// association (<see cref="IInboundContext.TryClaimHeartbeatQuarantineNotice"/>). A node
        /// that sees every other member as unreachable sends only heartbeats. Without this, a node
        /// that missed the one-time notice from <see cref="ArteryRemoting.Quarantine"/> (for
        /// example, because the link was down) would never learn that it was quarantined. The rate
        /// limit keeps the outbound traffic to a quarantined peer small. See #9010.
        /// </description></item>
        /// <item><description>
        /// Any other message always causes a notice.
        /// </description></item>
        /// </list>
        ///
        /// <para>
        /// This result controls only the notice. The caller discards the message in all conditions.
        /// </para>
        ///
        /// <para>
        /// Two sites call this method: <c>OnPush</c> in this stage, and
        /// <c>ArteryInboundProcessingStage.ProcessFrameLaneMode</c> for the lane path. They use the
        /// same method so that they stay in agreement.
        /// </para>
        /// </summary>
        /// <param name="message">The discarded message.</param>
        /// <param name="context">The inbound context, which owns the heartbeat rate limit.</param>
        /// <param name="originUid">The quarantined uid that sent <paramref name="message"/>.</param>
        internal static bool ShouldNotifyOrigin(object message, IInboundContext context, long originUid)
        {
            if (message is ArteryQuarantined)
                return false;

            return !IsHeartbeat(message) || context.TryClaimHeartbeatQuarantineNotice(originUid);
        }

        /// <summary>
        /// The minimum time between two quarantine notices that heartbeats from the same
        /// quarantined peer trigger. See <see cref="ShouldNotifyOrigin"/>.
        /// </summary>
        internal static readonly TimeSpan HeartbeatNoticeInterval = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Tells the caller if <paramref name="message"/> is a heartbeat. Three groups of
        /// messages match:
        /// <list type="bullet">
        /// <item><description>
        /// The control-channel liveness messages <see cref="ArteryHeartbeat"/> and
        /// <see cref="ArteryHeartbeatRsp"/>.
        /// </description></item>
        /// <item><description>
        /// The <see cref="RemoteWatcher"/> heartbeats on the ordinary stream. They implement
        /// the marker interface <see cref="IPriorityMessage"/>.
        /// </description></item>
        /// <item><description>
        /// A <see cref="RemoteWatcher"/> heartbeat inside an <see cref="ActorSelectionMessage"/>.
        /// <see cref="RemoteWatcher"/> sends its heartbeat through an ActorSelection, so the
        /// heartbeat arrives in that wrapper.
        /// </description></item>
        /// </list>
        /// </summary>
        private static bool IsHeartbeat(object message) => message switch
        {
            ArteryHeartbeat => true,
            ArteryHeartbeatRsp => true,
            IPriorityMessage => true,
            ActorSelectionMessage { Message: IPriorityMessage } => true,
            _ => false
        };

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

                // Reactive re-notification, per the SHARED skip set (see ShouldNotifyOrigin's
                // remarks -- applies ONLY to whether we react, never to whether we drop: every
                // message from a quarantined uid is dropped either way, just below).
                if (ShouldNotifyOrigin(envelope.Message, _stage.Context, envelope.OriginUid))
                {
                    var origin = _stage.Context.TryResolveOriginAddress(envelope.OriginUid);
                    if (origin is not null)
                        _stage.Context.SendControl(
                            origin,
                            new ArteryQuarantined(_stage.Context.LocalAddress, envelope.OriginUid));
                }

                Pull(_stage.In); // drop message
            }

            public void OnPull() => Pull(_stage.In);

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnDownstreamFinish(Exception cause) => InternalOnDownstreamFinish(cause);
        }
    }
}
