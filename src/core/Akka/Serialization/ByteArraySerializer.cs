//-----------------------------------------------------------------------
// <copyright file="ByteArraySerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using Akka.Actor;

namespace Akka.Serialization
{
    /// <summary>
    /// This is a special <see cref="Serializer"/> that serializes and deserializes byte arrays only.
    /// Legacy byte-array APIs return the supplied array unchanged.
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

        /// <summary>
        /// Byte arrays preserve their legacy empty manifest for wire compatibility.
        /// </summary>
        public override string Manifest(object obj)
        {
            return string.Empty;
        }

        /// <summary>
        /// Returns the exact byte array length when <paramref name="obj"/> is a byte array.
        /// </summary>
        public override int SizeHint(object obj)
        {
            if (obj == null)
                return 0;
            if (obj is byte[] bytes)
                return bytes.Length;
            throw new NotSupportedException("The object to convert is not a byte array.");
        }

        /// <summary>
        /// Serializes the given byte array directly into <paramref name="writer"/>.
        /// </summary>
        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            if (obj == null)
                return 0;

            if (obj is not byte[] bytes)
                throw new NotSupportedException("The object to convert is not a byte array.");

            if (bytes.Length == 0)
                return 0;

            var span = writer.GetSpan(bytes.Length);
            bytes.CopyTo(span);
            writer.Advance(bytes.Length);
            return bytes.Length;
        }

        /// <summary>
        /// Deserializes bytes into a byte array.
        /// </summary>
        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
        {
            return bytes.ToArray();
        }

        /// <summary>
        /// Serializes the given object into a byte array
        /// </summary>
        /// <param name="obj">The object to serialize </param>
        /// <exception cref="NotSupportedException">
        /// This exception is thrown if the given <paramref name="obj"/> is not a byte array.
        /// </exception>
        /// <returns>A byte array containing the serialized object</returns>
        public override byte[] ToBinary(object obj)
        {
            if (obj == null)
                return null;
            if (obj is byte[] bytes)
                return bytes;
            throw new NotSupportedException("The object to convert is not a byte array.");
        }

        /// <summary>
        /// Deserializes a byte array into an object of type <paramref name="type"/>.
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <param name="type">The type of object contained in the array</param>
        /// <returns>The object contained in the array</returns>
        public override object FromBinary(byte[] bytes, Type type)
        {
            return bytes;
        }

        /// <summary>
        /// Deserializes a byte array without copying for legacy compatibility callers.
        /// </summary>
        public override object FromBinary(byte[] bytes, string manifest)
        {
            return bytes;
        }
    }
}
