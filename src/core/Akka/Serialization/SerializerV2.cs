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
    /// A serializer that writes directly into caller-owned buffers and reads from sequence-backed input.
    /// </summary>
    public abstract class SerializerV2
    {
        /// <summary>
        /// Returned by <see cref="SizeHint"/> when the serialized size cannot be cheaply predicted.
        /// </summary>
        public const int UnknownSize = -1;

        /// <summary>
        /// The actor system to associate with this serializer.
        /// </summary>
        protected readonly ExtendedActorSystem system;

        private readonly FastLazy<int> _value;

        /// <summary>
        /// Initializes a new instance of the <see cref="SerializerV2" /> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer.</param>
        protected SerializerV2(ExtendedActorSystem system)
        {
            this.system = system;
            _value = new FastLazy<int>(() => SerializerIdentifierHelper.GetSerializerIdentifierFromConfig(GetType(), system));
        }

        /// <summary>
        /// Completely unique value to identify this serializer implementation.
        /// </summary>
        public virtual int Identifier => _value.Value;

        /// <summary>
        /// Returns whether this serializer emits a manifest for compatibility callers.
        /// </summary>
        public virtual bool IncludeManifest => true;

        /// <summary>
        /// Returns the manifest used by this serializer for <paramref name="obj"/>.
        /// </summary>
        public abstract string Manifest(object obj);

        /// <summary>
        /// Returns a best-effort serialized size hint, or <see cref="UnknownSize"/> when unknown.
        /// </summary>
        public virtual int SizeHint(object obj) => UnknownSize;

        /// <summary>
        /// Serializes <paramref name="obj"/> into <paramref name="writer"/>.
        /// </summary>
        /// <returns>The number of payload bytes written.</returns>
        public abstract int Serialize(object obj, IBufferWriter<byte> writer);

        /// <summary>
        /// Deserializes <paramref name="bytes"/> using a serializer manifest.
        /// </summary>
        public abstract object Deserialize(ReadOnlySequence<byte> bytes, string manifest);

        /// <summary>
        /// Serializes the given object into a byte array for compatibility boundaries.
        /// </summary>
        public virtual byte[] ToBinary(object obj)
        {
            var sizeHint = SizeHint(obj);
            var writer = sizeHint > 0 ? new ArrayBufferWriter<byte>(sizeHint) : new ArrayBufferWriter<byte>();
            Serialize(obj, writer);
            return writer.WrittenMemory.ToArray();
        }

        /// <summary>
        /// Serializes the given object into a byte array and uses the given address to decorate serialized ActorRefs.
        /// </summary>
        public byte[] ToBinaryWithAddress(Address address, object obj)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            return Serialization.WithTransport(system, address, () => ToBinary(obj));
#pragma warning restore CS0618 // Type or member is obsolete
        }

        /// <summary>
        /// Deserializes a byte array using a serializer manifest.
        /// </summary>
        public virtual object FromBinary(byte[] bytes, string manifest)
        {
            return Deserialize(new ReadOnlySequence<byte>(bytes), manifest);
        }

        /// <summary>
        /// Deserializes a byte array using a type manifest.
        /// </summary>
        public virtual object FromBinary(byte[] bytes, Type? type)
        {
            return FromBinary(bytes, type?.TypeQualifiedName() ?? string.Empty);
        }

        /// <summary>
        /// Deserializes a byte array into an object.
        /// </summary>
        public T FromBinary<T>(byte[] bytes) => (T)FromBinary(bytes, typeof(T));
    }
}
