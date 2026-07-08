//-----------------------------------------------------------------------
// <copyright file="CompressionProtocol.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System.Collections.Generic;

namespace Akka.Remote.Artery.Compression
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Control-stream messages of the compression table-advertisement protocol, ported from Apache
    /// Pekko's <c>CompressionProtocol</c> (Apache 2.0). The <b>receiver</b> of traffic builds a table
    /// and advertises index-&gt;value mappings back to the <b>sender</b>, which installs it for outbound
    /// compression and replies with an Ack.
    ///
    /// <para>
    /// SCAFFOLD (feature/artery-ref-manifest-compression): these are plain shape records so the design
    /// compiles and can be reviewed. They are deliberately NOT yet <c>IArteryControlMessage</c> and NOT
    /// yet <c>[AkkaSerializable]</c> -- wiring them onto the control stream (marker interface, MessagePack
    /// manifest constants in <see cref="ArteryControlMessageSerializer"/>, dispatch in
    /// <see cref="ArteryRemoting"/>) is an implementation task, so nothing new touches the wire or the
    /// serializer's API-approval surface in this scaffold phase.
    /// </para>
    ///
    /// <para>
    /// WIRE-SHAPE SIMPLIFICATION vs Pekko (flagged for review): Pekko advertises two parallel lists
    /// (<c>keys: string[]</c>, <c>values: int[]</c>). Because the receiver assigns dense indices
    /// <c>0..N-1</c>, the .NET port advertises a single ordered <c>Table</c> where the list position IS
    /// the index -- equivalent, smaller, and impossible to send with a gap.
    /// </para>
    /// </summary>
    internal interface ICompressionAdvertisement
    {
        /// <summary>The advertising (receiving) system's unique address.</summary>
        UniqueAddress From { get; }

        /// <summary>The origin UID that will USE this table for outbound compression (the sender being advertised to).</summary>
        long OriginUid { get; }

        /// <summary>The advertised table's version, echoed back in the matching Ack and stamped into future envelopes.</summary>
        byte TableVersion { get; }

        /// <summary>The advertised values in index order: <c>Table[i]</c> is the value for compression index <c>i</c>.</summary>
        IReadOnlyList<string> Table { get; }
    }

    /// <summary>INTERNAL API. Receiver -&gt; sender: advertises an actor-ref (path-string) compression table.</summary>
    internal sealed record ActorRefCompressionAdvertisement(
        UniqueAddress From,
        long OriginUid,
        byte TableVersion,
        IReadOnlyList<string> Table) : ICompressionAdvertisement;

    /// <summary>
    /// INTERNAL API. Sender -&gt; receiver: confirms receipt of an <see cref="ActorRefCompressionAdvertisement"/>.
    /// The first message stamped with the new version also confirms it, but the explicit Ack is needed
    /// in case the sender uses none of the advertised refs (so no stamped message ever arrives).
    /// </summary>
    internal sealed record ActorRefCompressionAdvertisementAck(UniqueAddress From, byte TableVersion);

    /// <summary>INTERNAL API. Receiver -&gt; sender: advertises a class-manifest compression table.</summary>
    internal sealed record ClassManifestCompressionAdvertisement(
        UniqueAddress From,
        long OriginUid,
        byte TableVersion,
        IReadOnlyList<string> Table) : ICompressionAdvertisement;

    /// <summary>INTERNAL API. Sender -&gt; receiver: confirms receipt of a <see cref="ClassManifestCompressionAdvertisement"/>.</summary>
    internal sealed record ClassManifestCompressionAdvertisementAck(UniqueAddress From, byte TableVersion);
}
