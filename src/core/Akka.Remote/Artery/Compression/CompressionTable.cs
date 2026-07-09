//-----------------------------------------------------------------------
// <copyright file="CompressionTable.cs" company="Akka.NET Project">
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
    /// A versioned, immutable <c>value -&gt; index</c> compression table, ported from Apache Pekko's
    /// <c>org.apache.pekko.remote.artery.compress.CompressionTable</c> (Apache 2.0). This is the
    /// OUTBOUND-facing form: the Encoder holds one per destination system and calls
    /// <see cref="Compress"/> to turn a warm actor-path/manifest string into a small table index for
    /// a COMPRESSED envelope tag; a miss (<see cref="NotCompressedId"/>) falls back to a LITERAL tag.
    ///
    /// <para>
    /// Unlike Pekko (which keys on <c>ActorRef</c>), the .NET port keys on the already-serialized
    /// path/manifest <b>string</b> (see design.md Decision "string-keyed tables") -- the Encoder
    /// already works in path strings (<c>OutboundEnvelope.SenderPath/RecipientPath</c>), so keying on
    /// strings avoids resolving an <c>IActorRef</c> on the hot path and guarantees the sender's lookup
    /// key is byte-identical to what the receiver observed and advertised.
    /// </para>
    ///
    /// <para>
    /// SCAFFOLD (feature/artery-ref-manifest-compression): this type compiles and is unit-testable in
    /// isolation but is NOT yet consulted by <see cref="ArteryEnvelopeCodec"/> -- encode still emits
    /// LITERAL. Wiring it into the Encoder is a later task, gated on the full advertisement loop.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The compressed value type; a reference type so absence can be represented as null (Pekko's <c>T &gt;: Null</c>).</typeparam>
    internal sealed class CompressionTable<T> where T : class
    {
        /// <summary>Returned by <see cref="Compress"/> when the value is not in the table -- caller must emit a LITERAL tag. Mirrors Pekko <c>NotCompressedId = -1</c>.</summary>
        public const int NotCompressedId = -1;

        /// <summary>Lowest valid table version.</summary>
        public const byte MinVersion = 0;

        /// <summary>
        /// Highest valid table version. Versions live in <c>0..127</c> (the high bit is never set, so a
        /// version can never be confused with the <see cref="DecompressionTable{T}.DisabledVersion"/>
        /// <c>0xFF</c> sentinel) and wrap <c>127 -&gt; 0</c> -- see <see cref="IncrementVersion"/>.
        /// </summary>
        public const byte MaxVersion = 127;

        private readonly IReadOnlyDictionary<T, int> _dictionary;

        public CompressionTable(long originUid, byte version, IReadOnlyDictionary<T, int> dictionary)
        {
            OriginUid = originUid;
            Version = version;
            _dictionary = dictionary;
        }

        /// <summary>The 64-bit UID of the system that will USE this table for outbound compression (the origin stamped into its envelopes).</summary>
        public long OriginUid { get; }

        /// <summary>
        /// The table version stamped into the envelope header's actor-ref / manifest table-version
        /// byte. Valid versions are <c>0..127</c> and wrap <c>127 -&gt; 0</c>;
        /// <see cref="DecompressionTable{T}.DisabledVersion"/> (<c>0xFF</c>) is the reserved
        /// "compression disabled" sentinel (Pekko's <c>-1</c>).
        /// </summary>
        public byte Version { get; }

        /// <summary>The <c>value -&gt; index</c> mappings. Indices are dense (<c>0..N-1</c>) so the inverse is a flat array.</summary>
        public IReadOnlyDictionary<T, int> Dictionary => _dictionary;

        /// <summary>O(1) lookup: the compression index for <paramref name="value"/>, or <see cref="NotCompressedId"/> if absent.</summary>
        public int Compress(T value) => _dictionary.TryGetValue(value, out var idx) ? idx : NotCompressedId;

        /// <summary>Inverts this <c>value -&gt; index</c> table into the <c>index -&gt; value</c>
        /// <see cref="DecompressionTable{T}"/> the Decoder uses. Requires dense, gap-less indices
        /// starting at 0 (guaranteed by <c>BuildForAdvertisement</c>).</summary>
        public DecompressionTable<T> Invert()
        {
            if (_dictionary.Count == 0)
                return new DecompressionTable<T>(OriginUid, Version, System.Array.Empty<T>());

            var table = new T[_dictionary.Count];
            foreach (var kv in _dictionary)
                table[kv.Value] = kv.Key; // dense 0..N-1 index == array position

            return new DecompressionTable<T>(OriginUid, Version, table);
        }

        /// <summary>An empty version-0 table -- the initial outbound state (nothing compressed, everything LITERAL).</summary>
        public static CompressionTable<T> Empty { get; } =
            new CompressionTable<T>(0L, 0, new Dictionary<T, int>());

        /// <summary>
        /// Builds the OUTBOUND <c>value -&gt; index</c> table a sender installs when it receives a
        /// compression advertisement (design.md Decision 5): the advertised values arrive as a single
        /// ordered list where the list position IS the dense compression index, so entry <c>i</c> maps
        /// to index <c>i</c>. <paramref name="originUid"/> is the advertisement's origin UID (the
        /// system that will USE this table for outbound). A duplicate value keeps its LAST position
        /// (the receiver builds gap-less, duplicate-free tables, so this is only a defensive tie-break).
        /// </summary>
        public static CompressionTable<T> FromAdvertisement(long originUid, byte version, IReadOnlyList<T> orderedValues)
        {
            if (orderedValues is null)
                throw new System.ArgumentNullException(nameof(orderedValues));

            var dictionary = new Dictionary<T, int>(orderedValues.Count);
            for (var i = 0; i < orderedValues.Count; i++)
                dictionary[orderedValues[i]] = i;

            return new CompressionTable<T>(originUid, version, dictionary);
        }

        /// <summary>
        /// The next table version after <paramref name="version"/>, ported from Pekko's
        /// <c>incrementTableVersion</c>: valid versions cycle through <c>0..127</c> and wrap
        /// <c>127 -&gt; 0</c>. Advancing the disabled sentinel
        /// (<see cref="DecompressionTable{T}.DisabledVersion"/>, <c>0xFF</c>) yields the first real
        /// version, <see cref="MinVersion"/> (<c>0</c>), matching Pekko's <c>-1 -&gt; 0</c>.
        /// </summary>
        public static byte IncrementVersion(byte version) =>
            version >= MaxVersion ? MinVersion : (byte)(version + 1);

        public override string ToString() => $"CompressionTable({OriginUid},{Version},count={_dictionary.Count})";
    }
}
