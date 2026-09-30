//-----------------------------------------------------------------------
// <copyright file="SerializerV1Adapter.cs" company="Akka.NET Project">
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
    /// Adapts legacy <see cref="Serializer"/> implementations to the <see cref="SerializerV2"/> contract.
    /// </summary>
    public sealed class SerializerV1Adapter : SerializerV2
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SerializerV1Adapter"/> class.
        /// </summary>
        public SerializerV1Adapter(ExtendedActorSystem system, Serializer inner) : base(system)
        {
            Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary>
        /// The wrapped V1 serializer.
        /// </summary>
        public Serializer Inner { get; }

        /// <inheritdoc />
        public override int Identifier => Inner.Identifier;

        /// <inheritdoc />
        public override string Manifest(object obj)
        {
            return Inner switch
            {
                SerializerWithStringManifest stringManifest => stringManifest.Manifest(obj),
                { IncludeManifest: true } => obj.GetType().TypeQualifiedName(),
                _ => string.Empty
            };
        }

        /// <inheritdoc />
        public override int SizeHint(object obj) => UnknownSize;

        /// <inheritdoc />
        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            var bytes = Inner.ToBinary(obj) ?? Array.Empty<byte>();
            if (bytes.Length == 0)
                return 0;

            var span = writer.GetSpan(bytes.Length);
            bytes.CopyTo(span);
            writer.Advance(bytes.Length);
            return bytes.Length;
        }

        /// <inheritdoc />
        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
        {
            return FromBinary(bytes.ToArray(), manifest);
        }

        /// <inheritdoc />
        public override byte[] ToBinary(object obj)
        {
            return Inner.ToBinary(obj);
        }

        /// <inheritdoc />
        /// <remarks>Virtual dispatch on <see cref="Inner"/> resolves this to whatever override it actually has.</remarks>
        public override object FromBinary(byte[] bytes, string manifest)
        {
            return Inner.FromBinary(bytes, manifest);
        }

        /// <inheritdoc />
        public override object FromBinary(byte[] bytes, Type type)
        {
            return Inner.FromBinary(bytes, type);
        }
    }
}
