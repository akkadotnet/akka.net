//-----------------------------------------------------------------------
// <copyright file="IOutboundCompressionTables.cs" company="Akka.NET Project">
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
    /// The narrow, per-destination source of OUTBOUND compression tables the encode stage reads
    /// (design.md Decision 2). Implemented by <c>Association</c>, whose two immutable
    /// <see cref="CompressionTable{T}"/> references are swapped (via <c>Volatile.Write</c>) when an
    /// advertisement arrives and read (via <c>Volatile.Read</c>) once per message by
    /// <see cref="ArteryEncodeStage"/>.
    ///
    /// <para>
    /// Because each <see cref="CompressionTable{T}"/> is immutable, a lane always reads a consistent
    /// <c>(version, dictionary)</c> pair -- no torn read across the swap. The default
    /// <see cref="CompressionTable{T}.Empty"/> table (nothing advertised yet, or compression off)
    /// yields an all-miss lookup, i.e. LITERAL tags byte-identical to a no-compression build.
    /// </para>
    /// </summary>
    internal interface IOutboundCompressionTables
    {
        /// <summary>The current OUTBOUND actor-ref (path-string) compression table for this destination.</summary>
        CompressionTable<string> OutboundActorRefCompressionTable { get; }

        /// <summary>The current OUTBOUND class-manifest compression table for this destination.</summary>
        CompressionTable<string> OutboundManifestCompressionTable { get; }
    }
}
