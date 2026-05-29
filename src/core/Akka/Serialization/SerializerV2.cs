//-----------------------------------------------------------------------
// <copyright file="SerializerV2.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using Akka.Actor;
using Akka.Util;

namespace Akka.Serialization
{
    /// <summary>
    /// Buffer-aware base class for serializers that participate in zero-copy serialization paths.
    ///
    /// <para>
    /// SerializerV2's primary API is <see cref="Serialize(IBufferWriter{byte}, object)"/> and
    /// <see cref="Deserialize(ReadOnlySequence{byte}, string)"/>. Akka.IO's TCP layer and the
    /// upcoming Akka.Streams TCP transport flow <see cref="ReadOnlySequence{T}"/> and
    /// <see cref="IBufferWriter{T}"/> end-to-end, so serializers that implement this API directly
    /// can avoid the per-message <c>byte[]</c> allocation that <see cref="Serializer"/> requires.
    /// </para>
    ///
    /// <para>
    /// The <see cref="ToBinary(object)"/> / <see cref="FromBinary(byte[], string)"/> bridge methods
    /// are provided so existing call sites that still operate on <c>byte[]</c> (Akka.Remote's
    /// current transport, the persistence journal API) continue to work unchanged. The bridges are
    /// virtual — subclasses (notably <see cref="SerializerV1Adapter"/>) override them to delegate
    /// directly to the underlying <c>byte[]</c>-native implementation rather than round-tripping
    /// through <see cref="ArrayBufferWriter{T}"/>.
    /// </para>
    ///
    /// <para>
    /// SerializerV2 deliberately does not extend <see cref="Serializer"/>. The two are independent
    /// hierarchies: V1 serializers are wrapped via <see cref="SerializerV1Adapter"/> when registered
    /// with the <see cref="Serialization"/> infrastructure, and V2 serializers are stored directly.
    /// </para>
    /// </summary>
    public abstract class SerializerV2
    {
        private readonly FastLazy<int> _identifier;

        /// <summary>
        /// The actor system this serializer is associated with.
        /// </summary>
        protected ExtendedActorSystem System { get; }

        /// <summary>
        /// Initializes a new instance of <see cref="SerializerV2"/>.
        /// </summary>
        /// <param name="system">The actor system this serializer is associated with.</param>
        protected SerializerV2(ExtendedActorSystem system)
        {
            System = system;
            _identifier = new FastLazy<int>(() => SerializerIdentifierHelper.GetSerializerIdentifierFromConfig(GetType(), system));
        }

        /// <summary>
        /// Completely unique value to identify this implementation of <see cref="SerializerV2"/>,
        /// used to optimize network traffic. Values from 0 to 16 are reserved for Akka internal usage.
        /// Override to hard-code the identifier; the default looks the value up from
        /// <c>akka.actor.serialization-identifiers</c>.
        /// </summary>
        public virtual int Identifier => _identifier.Value;

        /// <summary>
        /// Returns the manifest (type hint) associated with the given object.
        ///
        /// <para>
        /// Unlike V1, all V2 serializers expose a manifest unconditionally. Returning
        /// <see cref="string.Empty"/> indicates "no manifest needed for this object" — callers
        /// (e.g. Akka.Remote's wire format) decide whether to include an empty manifest field
        /// on the wire.
        /// </para>
        /// </summary>
        /// <param name="o">The object whose manifest is requested.</param>
        /// <returns>The manifest string, or <see cref="string.Empty"/> if no manifest is needed.</returns>
        public abstract string Manifest(object o);

        /// <summary>
        /// Returns a hint at the encoded size in bytes for the given object, used to size buffers
        /// before serialization. Implementations should return their best estimate; over-estimation
        /// is harmless (extra capacity), under-estimation forces a buffer grow.
        /// </summary>
        /// <param name="o">The object whose encoded size is being estimated.</param>
        /// <returns>An estimate of the encoded byte length.</returns>
        public virtual int SizeHint(object o) => 256;

        /// <summary>
        /// Serializes the given object directly into the provided buffer writer, avoiding the
        /// intermediate <c>byte[]</c> allocation that the <see cref="ToBinary(object)"/> bridge
        /// requires.
        /// </summary>
        /// <param name="buffer">The buffer to write into.</param>
        /// <param name="obj">The object to serialize.</param>
        /// <returns>The number of bytes written to the buffer.</returns>
        public abstract int Serialize(IBufferWriter<byte> buffer, object obj);

        /// <summary>
        /// Deserializes an object from the given byte sequence.
        ///
        /// <para>
        /// Implementations should handle multi-segment <see cref="ReadOnlySequence{T}"/> input
        /// correctly — typically via <see cref="SequenceReader{T}"/> for fixed-width fields, or
        /// via APIs that natively accept <see cref="ReadOnlySequence{T}"/> (e.g.
        /// <c>Encoding.UTF8.GetString(ReadOnlySequence&lt;byte&gt;)</c>).
        /// </para>
        /// </summary>
        /// <param name="buffer">The byte sequence containing the serialized object.</param>
        /// <param name="manifest">The manifest hint, or <see cref="string.Empty"/>.</param>
        /// <returns>The deserialized object.</returns>
        public abstract object Deserialize(ReadOnlySequence<byte> buffer, string manifest);

        /// <summary>
        /// V1-compatible bridge. Allocates an <see cref="ArrayBufferWriter{T}"/>, calls
        /// <see cref="Serialize(IBufferWriter{byte}, object)"/>, and returns the written bytes.
        /// Subclasses MAY override this to skip the round trip when they're already byte[]-native
        /// internally (e.g. <see cref="SerializerV1Adapter"/>).
        /// </summary>
        /// <param name="obj">The object to serialize.</param>
        /// <returns>A byte array containing the serialized object.</returns>
        public virtual byte[] ToBinary(object obj)
        {
            var buffer = new ArrayBufferWriter<byte>(SizeHint(obj));
            var written = Serialize(buffer, obj);
            return buffer.WrittenSpan.Slice(0, written).ToArray();
        }

        /// <summary>
        /// V1-compatible bridge. Wraps <paramref name="bytes"/> in a single-segment
        /// <see cref="ReadOnlySequence{T}"/> and calls <see cref="Deserialize(ReadOnlySequence{byte}, string)"/>.
        /// Subclasses MAY override this to skip the wrapping when they're already byte[]-native
        /// internally (e.g. <see cref="SerializerV1Adapter"/>).
        /// </summary>
        /// <param name="bytes">The serialized object's bytes.</param>
        /// <param name="manifest">The manifest hint, or <see cref="string.Empty"/>.</param>
        /// <returns>The deserialized object.</returns>
        public virtual object FromBinary(byte[] bytes, string manifest)
            => Deserialize(new ReadOnlySequence<byte>(bytes), manifest ?? string.Empty);

        /// <summary>
        /// V1-compatible bridge that accepts a <see cref="Type"/> hint instead of a string manifest.
        /// The default implementation derives the manifest from <c>type.TypeQualifiedName()</c> and
        /// dispatches to <see cref="FromBinary(byte[], string)"/>. <see cref="SerializerV1Adapter"/>
        /// overrides this to delegate directly to the wrapped V1 serializer's
        /// <see cref="Serializer.FromBinary(byte[], Type)"/>.
        /// </summary>
        /// <param name="bytes">The serialized object's bytes.</param>
        /// <param name="type">The expected runtime type, or <c>null</c> if unspecified.</param>
        /// <returns>The deserialized object.</returns>
        public virtual object FromBinary(byte[] bytes, Type? type)
        {
            var manifest = type != null ? type.TypeQualifiedName() : string.Empty;
            return FromBinary(bytes, manifest);
        }

        /// <summary>
        /// Convenience generic overload of <see cref="FromBinary(byte[], string)"/>.
        /// </summary>
        public T FromBinary<T>(byte[] bytes, string manifest) => (T)FromBinary(bytes, manifest);

        /// <summary>
        /// Convenience generic overload that uses <typeparamref name="T"/> as the type hint.
        /// </summary>
        public T FromBinary<T>(byte[] bytes) => (T)FromBinary(bytes, typeof(T));

        /// <summary>
        /// Serializes the object and decorates serialized <see cref="IActorRef"/> instances using
        /// the given <paramref name="address"/>.
        /// </summary>
        public byte[] ToBinaryWithAddress(Address address, object obj)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            return Serialization.WithTransport(System, address, () => ToBinary(obj));
#pragma warning restore CS0618 // Type or member is obsolete
        }
    }
}
