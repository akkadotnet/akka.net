//-----------------------------------------------------------------------
// <copyright file="ByteArraySerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using Akka.Actor;

namespace Akka.Serialization
{
    /// <summary>
    /// A <see cref="SerializerV2"/> that serializes and deserializes byte arrays as the identity
    /// transform — the byte array is the wire format. Serializer ID is 4. Wire format is
    /// byte-identical to the legacy V1 implementation.
    /// </summary>
    public class ByteArraySerializer : SerializerV2
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ByteArraySerializer" /> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer. </param>
        public ByteArraySerializer(ExtendedActorSystem system)
            : base(system)
        {
        }

        /// <inheritdoc/>
        public override string Manifest(object o) => string.Empty;

        /// <inheritdoc/>
        public override int SizeHint(object o) => o is byte[] b ? b.Length : 0;

        /// <inheritdoc/>
        public override void Serialize(IBufferWriter<byte> buffer, object obj)
        {
            if (obj is byte[] bytes)
            {
                buffer.Write(bytes);
                return;
            }
            throw new NotSupportedException("The object to convert is not a byte array.");
        }

        /// <inheritdoc/>
        public override object Deserialize(ReadOnlySequence<byte> buffer, string manifest)
        {
            // Materialize a fresh array — callers may retain the returned reference, so we cannot
            // alias to the (potentially pooled) backing memory of the sequence.
            return buffer.IsSingleSegment
                ? buffer.First.ToArray()
                : buffer.ToArray();
        }

        /// <inheritdoc/>
        public override byte[] ToBinary(object obj)
        {
            if (obj is byte[] bytes)
                return bytes;
            throw new NotSupportedException("The object to convert is not a byte array.");
        }

        /// <inheritdoc/>
        public override object FromBinary(byte[] bytes, string manifest) => bytes;

        /// <inheritdoc/>
        public override object FromBinary(byte[] bytes, Type? type) => bytes;
    }
}
