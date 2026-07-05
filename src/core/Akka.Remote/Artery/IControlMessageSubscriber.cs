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
    /// <b>GROUP7:</b> reliable system-message delivery's <c>SystemMessageAcker</c>/ACK-NACK
    /// stages are expected to subscribe here too, once they land -- this hook is deliberately
    /// generic (uid + raw message) rather than tailored to any one consumer.
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
