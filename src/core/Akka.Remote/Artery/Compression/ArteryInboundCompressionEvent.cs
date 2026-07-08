//-----------------------------------------------------------------------
// <copyright file="ArteryInboundCompressionEvent.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

namespace Akka.Remote.Artery.Compression
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// The lifecycle phase an <see cref="ArteryInboundCompressionEvent"/> reports for a single
    /// per-origin compression table on the RECEIVER side.
    /// </summary>
    internal enum ArteryInboundCompressionPhase
    {
        /// <summary>The receiver built a fresh table from its heavy hitters and sent it (or a resend) to the origin over the control stream.</summary>
        Advertised,

        /// <summary>The advertised table was confirmed (explicit Ack, first stamped message, or resend give-up) and is now the ACTIVE decompression table.</summary>
        Activated,

        /// <summary>The receiver successfully resolved a COMPRESSED tag against this table -- fired once per (origin, category, version), proving the table is in live use on the wire.</summary>
        Resolved
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// A low-frequency observability signal published to the <see cref="Akka.Event.EventStream"/> by the
    /// RECEIVER-side inbound compression coordinator (<see cref="InboundCompressionsImpl"/>) as a
    /// per-origin table moves through its lifecycle. This is the .NET analogue of Pekko's
    /// <c>Received{ActorRef,ClassManifest}CompressionTable</c> flight-recorder/event hooks (design.md
    /// "artery-ref-manifest-compression", Q7): it exists so tests and ops can observe the
    /// advertise -&gt; confirm -&gt; resolve loop closing without reaching into stage-private state.
    ///
    /// <para>
    /// Each phase fires <b>at most once per (origin, category, version)</b> -- <see cref="ArteryInboundCompressionPhase.Advertised"/>
    /// once per fresh build (resends are not re-published), <see cref="ArteryInboundCompressionPhase.Activated"/>
    /// once when the active version flips to this one, and <see cref="ArteryInboundCompressionPhase.Resolved"/>
    /// on the first successful decompression at this active version -- so it is safe to leave on in
    /// production. It is INTERNAL (no public-API surface, no wire presence).
    /// </para>
    /// </summary>
    /// <param name="OriginUid">The 64-bit UID of the sending system this table decodes / advertises to.</param>
    /// <param name="Version">The table version this event concerns.</param>
    /// <param name="IsManifest"><see langword="true"/> for the class-manifest table, <see langword="false"/> for the actor-ref table.</param>
    /// <param name="Phase">Which lifecycle transition this event reports.</param>
    /// <param name="EntryCount">For <see cref="ArteryInboundCompressionPhase.Advertised"/>, the number of entries in the advertised table; otherwise 0.</param>
    internal sealed record ArteryInboundCompressionEvent(
        long OriginUid,
        byte Version,
        bool IsManifest,
        ArteryInboundCompressionPhase Phase,
        int EntryCount = 0);
}
