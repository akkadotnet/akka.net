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
    public abstract class SerializerV2 : Serializer
    {
        /// <summary>
        /// Returned by <see cref="SizeHint"/> when the serialized size cannot be cheaply predicted.
        /// </summary>
        public const int UnknownSize = -1;

        /// <summary>
        /// Initializes a new instance of the <see cref="SerializerV2" /> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer.</param>
        protected SerializerV2(ExtendedActorSystem system) : base(system)
        {
        }

        /// <summary>
        /// V2 serializers use manifest-aware dispatch. New V2 serializers should emit
        /// non-empty manifests; legacy serializer-id ports may preserve empty manifests.
        /// </summary>
        public sealed override bool IncludeManifest => true;

        /// <summary>
        /// Returns the manifest used by this serializer for <paramref name="obj"/>.
        /// </summary>
        public abstract override string Manifest(object obj);

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
        public override byte[] ToBinary(object obj)
        {
            var sizeHint = SizeHint(obj);
            var writer = sizeHint > 0 ? new ArrayBufferWriter<byte>(sizeHint) : new ArrayBufferWriter<byte>();
            Serialize(obj, writer);
            return writer.WrittenMemory.ToArray();
        }

        /// <summary>
        /// Deserializes a byte array using a serializer manifest.
        /// </summary>
        public override object FromBinary(byte[] bytes, string manifest)
        {
            return Deserialize(new ReadOnlySequence<byte>(bytes), manifest);
        }

        /// <summary>
        /// Deserializes a byte array using a type manifest.
        /// </summary>
        public override object FromBinary(byte[] bytes, Type type)
        {
            return FromBinary(bytes, type?.TypeQualifiedName() ?? string.Empty);
        }
    }
}
