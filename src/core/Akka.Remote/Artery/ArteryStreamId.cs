//-----------------------------------------------------------------------
// <copyright file="ArteryStreamId.cs" company="Akka.NET Project">
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
    /// Identifies which of the three Artery TCP streams a connection carries, per the
    /// connection preamble described in <c>openspec/changes/artery-tcp-remoting/design.md</c>
    /// ("Envelope wire layout" / Decision 3). The value is written as a single byte
    /// immediately following the <c>AKKA</c> magic in <see cref="ArteryConnectionHeader"/>.
    /// </summary>
    internal enum ArteryStreamId : byte
    {
        /// <summary>
        /// Handshake, heartbeats, system-message ACK/NACK, and quarantine notifications.
        /// This stream pierces quarantine and must never be starved by user traffic.
        /// </summary>
        Control = 1,

        /// <summary>
        /// User (application) messages, partitioned across inbound/outbound lanes by
        /// recipient hash. Blocked while the association is quarantined, except for
        /// <c>ActorSelectionMessage</c> / <c>ClearSystemMessageDelivery</c>.
        /// </summary>
        Ordinary = 2,

        /// <summary>
        /// Messages destined for <c>large-message-destinations</c>, isolated onto their own
        /// stream to avoid head-of-line blocking. This is isolation, not chunking — Artery
        /// TCP does not fragment oversized payloads.
        /// </summary>
        Large = 3
    }
}
