//-----------------------------------------------------------------------
// <copyright file="CompressionAdvertisementFormatters.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System.Collections.Generic;
using Akka.Serialization.V2;
using MessagePack;

namespace Akka.Remote.Artery.Compression
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// <see cref="IAkkaMessagePackFormatter{T}"/> for the compression advertisement's <c>byte</c>
    /// table-version field. The <c>Akka.Serialization.V2</c> source generator has no native
    /// <c>System.Byte</c> field kind, so -- exactly like <see cref="AddressFormatter"/> for
    /// <see cref="Akka.Actor.Address"/> -- the byte is carried through this formatter, registered on
    /// <see cref="ArteryControlMessageSerializer"/> via <see cref="AkkaSerializerFormatterAttribute"/>.
    ///
    /// <para>
    /// The value (a compression table version, <c>0..127</c>, or the <c>0xFF</c> disabled sentinel) is
    /// written as a single MessagePack integer, so the encoded size is exactly that of the equivalent
    /// <c>int</c> field.
    /// </para>
    /// </summary>
    internal sealed class CompressionTableVersionFormatter : IAkkaMessagePackFormatter<byte>
    {
        /// <inheritdoc/>
        public void Write(ref MessagePackWriter writer, byte value) => writer.Write((int)value);

        /// <inheritdoc/>
        public byte Read(ref MessagePackReader reader) => (byte)reader.ReadInt32();

        /// <inheritdoc/>
        public int SizeOf(byte value) => MessagePackSizes.SizeOfInt32(value);
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// <see cref="IAkkaMessagePackFormatter{T}"/> for a <see cref="CompressionAdvertisementTable"/> --
    /// the ordered list of advertised values (position = compression index). Written as ONE top-level
    /// MessagePack array of strings (satisfying the formatter contract's "exactly one top-level value"
    /// rule), so the list position is the index with no separate <c>values[]</c> array (design.md
    /// Decision 5).
    /// </summary>
    internal sealed class CompressionAdvertisementTableFormatter : IAkkaMessagePackFormatter<CompressionAdvertisementTable>
    {
        /// <inheritdoc/>
        public void Write(ref MessagePackWriter writer, CompressionAdvertisementTable value)
        {
            writer.WriteArrayHeader(value.Count);
            for (var i = 0; i < value.Count; i++)
                writer.Write(value[i]);
        }

        /// <inheritdoc/>
        public CompressionAdvertisementTable Read(ref MessagePackReader reader)
        {
            var count = reader.ReadArrayHeader();
            if (count == 0)
                return CompressionAdvertisementTable.Empty;

            var values = new string[count];
            for (var i = 0; i < count; i++)
                values[i] = reader.ReadString() ?? string.Empty;

            return new CompressionAdvertisementTable(values);
        }

        /// <inheritdoc/>
        public int SizeOf(CompressionAdvertisementTable value)
        {
            var size = MessagePackSizes.SizeOfArrayHeader(value.Count);
            for (var i = 0; i < value.Count; i++)
                size += MessagePackSizes.SizeOfString(value[i]);
            return size;
        }
    }
}
