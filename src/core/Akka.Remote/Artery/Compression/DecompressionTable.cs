//-----------------------------------------------------------------------
// <copyright file="DecompressionTable.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;

namespace Akka.Remote.Artery.Compression
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// A versioned, immutable <c>index -&gt; value</c> decompression table, ported from Apache Pekko's
    /// <c>org.apache.pekko.remote.artery.compress.DecompressionTable</c> (Apache 2.0). This is the
    /// INBOUND-facing form: the Decoder holds a small rotation of these per origin UID and calls
    /// <see cref="Get"/> to resolve a COMPRESSED envelope index back to its actor-path/manifest string,
    /// completing the "resolve a compressed index -&gt; value" step that <see cref="ArteryEnvelopeDecoded"/>
    /// currently defers.
    ///
    /// <para>
    /// SCAFFOLD (feature/artery-ref-manifest-compression): compiles and is unit-testable but not yet
    /// consulted by the inbound stage -- COMPRESSED tags are still dropped on decode until the loop lands.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The compressed value type; a reference type (null == unallocated slot, per Pekko's <c>T &gt;: Null</c>).</typeparam>
    internal sealed class DecompressionTable<T> where T : class
    {
        /// <summary>
        /// Reserved "compression disabled" version sentinel. Pekko uses signed <c>-1</c>; the .NET port
        /// stores the version in an unsigned wire byte, so the sentinel is <c>0xFF</c> -- distinct from
        /// every valid version (<c>0..127</c>, high bit never set).
        /// </summary>
        public const byte DisabledVersion = 0xFF;

        private readonly T?[] _table;

        public DecompressionTable(long originUid, byte version, T?[] table)
        {
            OriginUid = originUid;
            Version = version;
            _table = table;
        }

        /// <summary>The 64-bit UID of the origin system this table decodes messages from.</summary>
        public long OriginUid { get; }

        /// <summary>This table's version, matched against the envelope header's table-version byte.</summary>
        public byte Version { get; }

        /// <summary>Number of allocated indices.</summary>
        public int Length => _table.Length;

        /// <summary>
        /// Resolves index <paramref name="idx"/> to its value. Throws when the index is outside the
        /// allocated range -- the caller (Decoder) treats that as "stale/unknown table" and drops the
        /// message with a warning rather than faulting the stream (Pekko <c>UnknownCompressedIdException</c>).
        /// </summary>
        public T Get(int idx)
        {
            if ((uint)idx >= (uint)_table.Length)
                throw new ArgumentOutOfRangeException(
                    nameof(idx), idx,
                    $"Attempted decompression of unknown id [{idx}]; only {_table.Length} ids allocated in table version [{Version}] for origin [{OriginUid}].");

            var value = _table[idx];
            if (value is null)
                throw new ArgumentOutOfRangeException(
                    nameof(idx), idx,
                    $"Compression index [{idx}] is unallocated in table version [{Version}] for origin [{OriginUid}].");

            return value;
        }

        /// <summary>An empty active table at version 0 -- the initial inbound state before any advertisement.</summary>
        public static DecompressionTable<T> Empty { get; } =
            new DecompressionTable<T>(0L, 0, Array.Empty<T?>());

        /// <summary>An empty table marked <see cref="DisabledVersion"/> -- the initial "no compression yet" sentinel kept in the old-tables list.</summary>
        public static DecompressionTable<T> Disabled { get; } =
            new DecompressionTable<T>(0L, DisabledVersion, Array.Empty<T?>());

        public override string ToString() => $"DecompressionTable({OriginUid},{Version},count={_table.Length})";
    }
}
