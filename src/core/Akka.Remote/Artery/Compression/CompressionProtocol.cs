//-----------------------------------------------------------------------
// <copyright file="CompressionProtocol.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using Akka.Serialization.V2;

namespace Akka.Remote.Artery.Compression
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Common shape of the two compression table-advertisement control messages
    /// (<see cref="ActorRefCompressionAdvertisement"/> / <see cref="ClassManifestCompressionAdvertisement"/>),
    /// ported from Apache Pekko's <c>CompressionProtocol</c> (Apache 2.0). The <b>receiver</b> of
    /// traffic builds a table and advertises index-&gt;value mappings back to the <b>sender</b>, which
    /// installs it as its OUTBOUND compression table for that destination and replies with an Ack.
    ///
    /// <para>
    /// WIRE-SHAPE SIMPLIFICATION vs Pekko (design.md Decision 5): Pekko advertises two parallel lists
    /// (<c>keys: string[]</c>, <c>values: int[]</c>). Because the receiver assigns dense indices
    /// <c>0..N-1</c>, the .NET port advertises a single ordered <see cref="Table"/> where the list
    /// position IS the index -- equivalent, smaller, and impossible to send with a gap.
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

    /// <summary>
    /// INTERNAL API.
    ///
    /// The ordered list of advertised values (position = compression index) carried by a
    /// <see cref="ICompressionAdvertisement"/>, wrapped in a non-generic, formatter-backed carrier.
    ///
    /// <para>
    /// <b>Why a wrapper (design.md Decision 5 escape hatch).</b> The advertisement wire form the design
    /// mandates is a single ordered <c>IReadOnlyList&lt;string&gt;</c>. The <c>Akka.Serialization.V2</c>
    /// source generator has no native collection-field support, and an
    /// <see cref="AkkaSerializerFormatterAttribute"/> target must be a non-generic, non-array named type
    /// (so <c>IReadOnlyList&lt;string&gt;</c> / <c>string[]</c> can be neither a generated field nor a
    /// formatter target directly). This tiny non-generic carrier is therefore the formatter target --
    /// the exact same <see cref="AddressFormatter"/>-style escape hatch the design names for
    /// <see cref="Akka.Actor.Address"/>. Its wire form (a MessagePack array of strings) lives in
    /// <see cref="CompressionAdvertisementTableFormatter"/>.
    /// </para>
    /// <para>
    /// Value equality (ordinal, sequence-wise) is implemented so the enclosing advertisement records
    /// remain value-comparable -- a decoded advertisement equals the original it round-tripped from.
    /// </para>
    /// </summary>
    internal sealed class CompressionAdvertisementTable : IReadOnlyList<string>, IEquatable<CompressionAdvertisementTable>
    {
        private readonly IReadOnlyList<string> _values;

        /// <summary>An empty advertised table (zero entries).</summary>
        public static CompressionAdvertisementTable Empty { get; } = new(Array.Empty<string>());

        public CompressionAdvertisementTable(IReadOnlyList<string> values) =>
            _values = values ?? throw new ArgumentNullException(nameof(values));

        /// <inheritdoc/>
        public string this[int index] => _values[index];

        /// <inheritdoc/>
        public int Count => _values.Count;

        /// <inheritdoc/>
        public IEnumerator<string> GetEnumerator() => _values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        public bool Equals(CompressionAdvertisementTable? other)
        {
            if (other is null)
                return false;
            if (ReferenceEquals(this, other))
                return true;
            if (_values.Count != other._values.Count)
                return false;

            for (var i = 0; i < _values.Count; i++)
            {
                if (!string.Equals(_values[i], other._values[i], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as CompressionAdvertisementTable);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(_values.Count);
            foreach (var value in _values)
                hash.Add(value, StringComparer.Ordinal);
            return hash.ToHashCode();
        }

        public override string ToString() => $"CompressionAdvertisementTable(count={_values.Count})";
    }

    /// <summary>INTERNAL API. Receiver -&gt; sender: advertises an actor-ref (path-string) compression table.</summary>
    /// <param name="From">The advertising (receiving) system's unique address.</param>
    /// <param name="OriginUid">The origin UID that will USE this table for outbound compression (the sender being advertised to).</param>
    /// <param name="TableVersion">The advertised table's version.</param>
    /// <param name="Entries">The advertised values in index order (position = index).</param>
    [AkkaSerializable(Manifest = ArteryControlMessageSerializer.ActorRefCompressionAdvertisementManifest)]
    internal sealed record ActorRefCompressionAdvertisement(
        [property: AkkaField(1)] UniqueAddress From,
        [property: AkkaField(2)] long OriginUid,
        [property: AkkaField(3)] byte TableVersion,
        [property: AkkaField(4)] CompressionAdvertisementTable Entries)
        : IArteryControlMessage, ICompressionAdvertisement
    {
        /// <inheritdoc/>
        public IReadOnlyList<string> Table => Entries;
    }

    /// <summary>
    /// INTERNAL API. Sender -&gt; receiver: confirms receipt of an <see cref="ActorRefCompressionAdvertisement"/>.
    /// The first message stamped with the new version also confirms it, but the explicit Ack is needed
    /// in case the sender uses none of the advertised refs (so no stamped message ever arrives).
    /// </summary>
    /// <param name="From">The confirming (sending) system's own unique address.</param>
    /// <param name="TableVersion">The version being confirmed (echoed from the advertisement).</param>
    [AkkaSerializable(Manifest = ArteryControlMessageSerializer.ActorRefCompressionAdvertisementAckManifest)]
    internal sealed record ActorRefCompressionAdvertisementAck(
        [property: AkkaField(1)] UniqueAddress From,
        [property: AkkaField(2)] byte TableVersion) : IArteryControlMessage;

    /// <summary>INTERNAL API. Receiver -&gt; sender: advertises a class-manifest compression table.</summary>
    /// <param name="From">The advertising (receiving) system's unique address.</param>
    /// <param name="OriginUid">The origin UID that will USE this table for outbound compression (the sender being advertised to).</param>
    /// <param name="TableVersion">The advertised table's version.</param>
    /// <param name="Entries">The advertised values in index order (position = index).</param>
    [AkkaSerializable(Manifest = ArteryControlMessageSerializer.ClassManifestCompressionAdvertisementManifest)]
    internal sealed record ClassManifestCompressionAdvertisement(
        [property: AkkaField(1)] UniqueAddress From,
        [property: AkkaField(2)] long OriginUid,
        [property: AkkaField(3)] byte TableVersion,
        [property: AkkaField(4)] CompressionAdvertisementTable Entries)
        : IArteryControlMessage, ICompressionAdvertisement
    {
        /// <inheritdoc/>
        public IReadOnlyList<string> Table => Entries;
    }

    /// <summary>INTERNAL API. Sender -&gt; receiver: confirms receipt of a <see cref="ClassManifestCompressionAdvertisement"/>.</summary>
    /// <param name="From">The confirming (sending) system's own unique address.</param>
    /// <param name="TableVersion">The version being confirmed (echoed from the advertisement).</param>
    [AkkaSerializable(Manifest = ArteryControlMessageSerializer.ClassManifestCompressionAdvertisementAckManifest)]
    internal sealed record ClassManifestCompressionAdvertisementAck(
        [property: AkkaField(1)] UniqueAddress From,
        [property: AkkaField(2)] byte TableVersion) : IArteryControlMessage;
}
