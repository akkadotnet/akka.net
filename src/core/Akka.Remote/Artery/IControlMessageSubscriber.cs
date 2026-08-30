//-----------------------------------------------------------------------
// <copyright file="IControlMessageSubscriber.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// A subscriber for inbound Artery control-stream messages that are NOT the handshake
    /// protocol itself (<see cref="HandshakeReq"/>/<see cref="HandshakeRsp"/> are consumed
    /// entirely inside <see cref="InboundHandshakeStage"/> and never reach this hook). See
    /// <c>openspec/changes/artery-tcp-remoting/design.md</c> (task group 6, "Control Stream",
    /// task 6.2).
    ///
    /// <para>
    /// <c>ArteryRemoting</c> keeps a simple list of subscribers (see
    /// <c>ArteryRemoting.SubscribeControl</c>/<c>UnsubscribeControl</c>) and notifies all of them,
    /// in registration order, for every non-handshake control envelope decoded off ANY inbound
    /// control connection. At task group 6, <c>ArteryRemoting</c> subscribes to itself to handle
    /// <see cref="ArteryHeartbeat"/> (reply with <see cref="ArteryHeartbeatRsp"/>) and
    /// <see cref="ArteryQuarantined"/> (publish <see cref="ThisActorSystemQuarantinedEvent"/>).
    /// </para>
    /// <para>
    /// <b>GROUP7 RESOLVED (design.md gate G3):</b> <see cref="SystemMessageDeliveryStage"/> (the
    /// OUTBOUND half of reliable system-message delivery) subscribes here too, to observe
    /// <see cref="Ack"/>/<see cref="Nack"/> replies -- bridged into its own single-threaded
    /// <c>GraphStageLogic</c> execution via <c>GetAsyncCallback</c>, since notifications arrive from
    /// a different stream's execution context. <see cref="SystemMessageAckerStage"/> (the INBOUND
    /// half) does NOT subscribe here -- it is composed directly into the inbound pipeline instead,
    /// since it needs to see EVERY inbound envelope (including ordinary ones, to pass them through)
    /// rather than just the non-handshake control subset this hook exposes. This hook's generic
    /// shape (uid + raw message) needed no changes to support the new subscriber.
    /// </para>
    /// </summary>
    internal interface IControlMessageSubscriber
    {
        /// <summary>
        /// Called for every decoded non-handshake control message received from ANY inbound
        /// control connection.
        /// </summary>
        /// <param name="originUid">The sending system's uid, decoded from the envelope's fixed header.</param>
        /// <param name="message">The deserialized control message.</param>
        void ControlMessageReceived(long originUid, object message);
    }
}
