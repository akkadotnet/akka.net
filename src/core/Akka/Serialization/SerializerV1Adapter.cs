//-----------------------------------------------------------------------
// <copyright file="SerializerV1Adapter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Util;
using Akka.Util.Reflection;

namespace Akka.Serialization
{
    /// <summary>
    /// Wraps a legacy <see cref="Serializer"/> (or <see cref="SerializerWithStringManifest"/>) so it
    /// participates in the V2 dispatch infrastructure. The adapter routes V2 calls through the
    /// inner serializer's <c>byte[]</c>-based API, so wrapped V1 serializers run with the same
    /// allocation profile they had before — no faster, no slower.
    ///
    /// <para>
    /// All <see cref="Serialization.AddSerializer(string, Serializer)"/> registrations and HOCON
    /// <c>akka.actor.serializers</c> entries that resolve to a V1 type are auto-wrapped in this
    /// adapter on registration. <see cref="Inner"/> exposes the original V1 instance for callers
    /// that still need the V1-typed reference.
    /// </para>
    /// </summary>
    public sealed class SerializerV1Adapter : SerializerV2
    {
        private readonly Serializer _inner;
        private readonly SerializerWithStringManifest? _innerStringManifest;

        /// <summary>
        /// Initializes a new adapter wrapping <paramref name="inner"/>.
        /// </summary>
        /// <param name="system">The actor system this serializer is associated with.</param>
        /// <param name="inner">The legacy serializer to wrap.</param>
        public SerializerV1Adapter(ExtendedActorSystem system, Serializer inner) : base(system)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _innerStringManifest = inner as SerializerWithStringManifest;
        }

        /// <summary>
        /// The wrapped legacy serializer.
        /// </summary>
        public Serializer Inner => _inner;

        /// <inheritdoc/>
        public override int Identifier => _inner.Identifier;

        /// <inheritdoc/>
        public override string Manifest(object o)
        {
            if (_innerStringManifest is not null)
                return _innerStringManifest.Manifest(o);
            return _inner.IncludeManifest ? o.GetType().TypeQualifiedName() : string.Empty;
        }

        /// <inheritdoc/>
        public override void Serialize(IBufferWriter<byte> buffer, object obj)
        {
            // V1 is byte[]-native; we have to allocate once on this path. The V1-bridge overrides
            // below ensure that callers reaching us through ToBinary/FromBinary don't pay an
            // additional round trip through ArrayBufferWriter.
            var bytes = _inner.ToBinary(obj);
            buffer.Write(bytes);
        }

        /// <inheritdoc/>
        public override object Deserialize(ReadOnlySequence<byte> buffer, string manifest)
        {
            var bytes = buffer.IsSingleSegment
                ? buffer.First.ToArray()
                : buffer.ToArray();
            return FromBinary(bytes, manifest);
        }

        /// <inheritdoc/>
        public override byte[] ToBinary(object obj) => _inner.ToBinary(obj);

        /// <inheritdoc/>
        public override object FromBinary(byte[] bytes, string manifest)
        {
            if (_innerStringManifest is not null)
                return _innerStringManifest.FromBinary(bytes, manifest ?? string.Empty);

            if (string.IsNullOrEmpty(manifest))
                return _inner.FromBinary(bytes, null);

            Type type;
            try
            {
                type = TypeCache.GetType(manifest);
            }
            catch (Exception ex)
            {
                throw new SerializationException(
                    $"Cannot find manifest class [{manifest}] for serializer [{_inner.GetType().FullName}] (id [{_inner.Identifier}]).",
                    ex);
            }

            return _inner.FromBinary(bytes, type);
        }

        /// <inheritdoc/>
        public override object FromBinary(byte[] bytes, Type? type) => _inner.FromBinary(bytes, type);
    }
}
