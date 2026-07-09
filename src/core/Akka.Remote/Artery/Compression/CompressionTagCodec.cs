//-----------------------------------------------------------------------
// <copyright file="CompressionTagCodec.cs" company="Akka.NET Project">
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
    /// Pure, allocation-free glue between a compression table and the Artery envelope's 32-bit tag
    /// scheme (<see cref="ArteryEnvelopeHeader"/>). These are the encode/decode HOOKS that the
    /// Encoder and Decoder will call once compression is wired up; they are unit-testable in
    /// isolation.
    ///
    /// <para>
    /// SCAFFOLD (feature/artery-ref-manifest-compression): these helpers exist and are correct, but
    /// are NOT yet called from <see cref="ArteryEnvelopeCodec"/>. Encode still emits LITERAL and
    /// decode still drops COMPRESSED tags until the full advertisement loop + tests exist. Flipping
    /// the Encoder to call <see cref="TryBuildActorRefTag"/> / <see cref="TryBuildManifestTag"/> is a
    /// deliberately separate, gated step.
    /// </para>
    /// </summary>
    internal static class CompressionTagCodec
    {
        /// <summary>The maximum index a 16-bit COMPRESSED tag can carry (<see cref="ArteryEnvelopeHeader.CompressedIndexMask"/>).</summary>
        public const int MaxIndex = (int)ArteryEnvelopeHeader.CompressedIndexMask; // 65535

        /// <summary>
        /// Builds the 32-bit COMPRESSED tag for a table index: the non-zero top-byte marker
        /// (<see cref="ArteryEnvelopeHeader.CompressedTagMarker"/>) OR'd with the 16-bit index. The
        /// table version travels separately in the header's table-version byte, not in the tag.
        /// </summary>
        public static uint MakeCompressedTag(int index)
        {
            if ((uint)index > (uint)MaxIndex)
                throw new System.ArgumentOutOfRangeException(
                    nameof(index), index, $"Compression index must be in 0..{MaxIndex} to fit a 16-bit COMPRESSED tag.");

            return ArteryEnvelopeHeader.CompressedTagMarker | (uint)index;
        }

        /// <summary>
        /// ENCODE HOOK. If <paramref name="value"/> is present in <paramref name="table"/>, sets
        /// <paramref name="tag"/> to its COMPRESSED tag and <paramref name="tableVersion"/> to the
        /// table version, and returns <see langword="true"/>. Otherwise returns <see langword="false"/>
        /// and the caller must emit a LITERAL. A null table (compression not established for this
        /// destination) always returns <see langword="false"/>.
        /// </summary>
        public static bool TryBuildActorRefTag(CompressionTable<string>? table, string? value, out uint tag, out byte tableVersion) =>
            TryBuildTag(table, value, out tag, out tableVersion);

        /// <summary>ENCODE HOOK. Manifest counterpart of <see cref="TryBuildActorRefTag"/>.</summary>
        public static bool TryBuildManifestTag(CompressionTable<string>? table, string? value, out uint tag, out byte tableVersion) =>
            TryBuildTag(table, value, out tag, out tableVersion);

        private static bool TryBuildTag(CompressionTable<string>? table, string? value, out uint tag, out byte tableVersion)
        {
            tag = ArteryEnvelopeHeader.AbsentTag;
            tableVersion = 0;

            if (table is null || string.IsNullOrEmpty(value))
                return false;

            var idx = table.Compress(value!);
            if (idx == CompressionTable<string>.NotCompressedId)
                return false;

            tag = MakeCompressedTag(idx);
            tableVersion = table.Version;
            return true;
        }

        /// <summary>
        /// DECODE HOOK. Resolves a COMPRESSED index against <paramref name="table"/>. Returns
        /// <see langword="false"/> when the table is null or the index is unallocated/stale, which the
        /// Decoder treats as "drop this message and let a fresh table be advertised" rather than a
        /// hard fault.
        /// </summary>
        public static bool TryResolve<T>(DecompressionTable<T>? table, int idx, out T value) where T : class
        {
            if (table is not null && table.TryGet(idx, out var resolved))
            {
                value = resolved!;
                return true;
            }

            value = null!;
            return false;
        }
    }
}
