//-----------------------------------------------------------------------
// <copyright file="ArteryControlMessages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using Akka.Serialization.V2;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Liveness heartbeat sent by the control stream when it has otherwise been idle for
    /// <c>akka.remote.artery.advanced.control-heartbeat-interval</c> -- see
    /// <see cref="ArteryHeartbeatStage"/> and <c>openspec/changes/artery-tcp-remoting/design.md</c>
    /// (task group 6, "Control Stream", tasks 6.4/6.6). Carries no payload; its mere arrival is
    /// the signal. The receiver replies with <see cref="ArteryHeartbeatRsp"/>
    /// (<c>ArteryRemoting</c>'s own <c>IControlMessageSubscriber</c>).
    ///
    /// <para>
    /// Deliberately fieldless: opts into codegen via <see cref="AkkaSerializableAttribute.AllowEmpty"/>
    /// (sourcegen gap fix #8331) -- arrival IS the signal, with nothing to carry.
    /// </para>
    /// </summary>
    [AkkaSerializable(Manifest = ArteryControlMessageSerializer.HeartbeatManifest, AllowEmpty = true)]
    internal sealed record ArteryHeartbeat : IArteryControlMessage;

    /// <summary>
    /// INTERNAL API.
    ///
    /// Reply to <see cref="ArteryHeartbeat"/>. No action is taken on receipt beyond dispatch to
    /// any subscribed <see cref="IControlMessageSubscriber"/> at task group 6 -- a
    /// missed-heartbeat failure detector is later (group 7+) work.
    /// </summary>
    [AkkaSerializable(Manifest = ArteryControlMessageSerializer.HeartbeatRspManifest, AllowEmpty = true)]
    internal sealed record ArteryHeartbeatRsp : IArteryControlMessage;

    /// <summary>
    /// INTERNAL API.
    ///
    /// Sent by <c>ArteryRemoting.Quarantine</c> to notify the peer that THIS system has
    /// quarantined the association identified by <paramref name="QuarantinedUid"/> -- see
    /// design.md ("Handshake + association/UID (gate G2)", "Quarantine (UID-scoped)") and task
    /// group 6 task 6.5. Sent over (and, "pierces quarantine", received over) the control
    /// stream/channel, which stays open even for a quarantined association.
    /// </summary>
    /// <param name="From">The unique address of the system that performed the quarantine (the sender of this message).</param>
    /// <param name="QuarantinedUid">
    /// The uid that was quarantined, from the quarantining system's point of view. The receiver
    /// only acts on this (publishing a <see cref="ThisActorSystemQuarantinedEvent"/>-equivalent)
    /// when it matches the receiver's OWN current uid -- a notification about a stale/superseded
    /// incarnation of the receiver must not be acted on.
    /// </param>
    [AkkaSerializable(Manifest = ArteryControlMessageSerializer.QuarantinedManifest)]
    internal sealed record ArteryQuarantined(
        [property: AkkaField(1)] UniqueAddress From,
        [property: AkkaField(2)] long QuarantinedUid) : IArteryControlMessage;
}
