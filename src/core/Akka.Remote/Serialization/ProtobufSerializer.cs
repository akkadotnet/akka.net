//-----------------------------------------------------------------------
// <copyright file="ProtobufSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Serialization;
using Akka.Util;
using Google.Protobuf;

namespace Akka.Remote.Serialization
{
    /// <summary>
    /// This is a special <see cref="Serializer"/> that serializes and deserializes Google protobuf messages only.
    /// </summary>
    public class ProtobufSerializer : Serializer
    {
        private static readonly ConcurrentDictionary<string, MessageParser> TypeLookup = new();
        private readonly Dictionary<string, MessageParser> _registeredParsers = new(StringComparer.Ordinal);

        /// <summary>
        /// Initializes a new instance of the <see cref="ProtobufSerializer"/> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer. </param>
        public ProtobufSerializer(ExtendedActorSystem system) : base(system)
        {
            var setup = system.Settings.Setup.Get<ProtobufSerializerSetup>();
            if (!setup.HasValue)
                return;

            foreach (var descriptor in setup.Value.MessageDescriptors)
            {
                _registeredParsers[descriptor.ClrType.TypeQualifiedName()] = descriptor.Parser;
                _registeredParsers[descriptor.ClrType.FullName!] = descriptor.Parser;
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// A subclass keeps resolving its own id from HOCON via <see cref="Serializer.Identifier"/>.
        /// </remarks>
        public override int Identifier => GetType() == typeof(ProtobufSerializer) ? 2 : base.Identifier;

        /// <inheritdoc />
        public override bool IncludeManifest => true;

        /// <inheritdoc />
        public override byte[] ToBinary(object obj)
        {
            var message = obj as IMessage;
            if (message != null)
            {
                return message.ToByteArray();
            }

            throw new ArgumentException($"Can't serialize a non-protobuf message using protobuf [{obj.GetType().TypeQualifiedName()}]");
        }

        /// <inheritdoc />
        public override object FromBinary(byte[] bytes, string manifest)
        {
            if (_registeredParsers.Count != 0 && manifest is not null
                && (_registeredParsers.TryGetValue(manifest, out var parser)
                    || _registeredParsers.TryGetValue(TypeExtensions.StripAssemblyIdentity(manifest), out parser)))
                return parser.ParseFrom(bytes);

            if (!AkkaFeatures.IsDynamicTypeLoadingSupported)
                throw UnregisteredMessage(manifest);

            return base.FromBinary(bytes, manifest);
        }

        /// <inheritdoc />
        public override object FromBinary(byte[] bytes, Type type)
        {
            if (_registeredParsers.TryGetValue(type.TypeQualifiedName(), out var parser))
                return parser.ParseFrom(bytes);

            if (!AkkaFeatures.IsDynamicTypeLoadingSupported)
                throw UnregisteredMessage(type.TypeQualifiedName());

            return FromBinaryReflecting(bytes, type);
        }

        private SerializationException UnregisteredMessage(string? manifest)
            => new($"Cannot deserialize Protobuf manifest [{manifest}] for serializer [{Identifier}] with Akka.DynamicTypeLoading off. " +
                   "Register the message descriptor through ProtobufSerializerSetup.");

        [RequiresUnreferencedCode("Constructs an unregistered Protobuf message by reflection to obtain its parser.")]
        private object FromBinaryReflecting(byte[] bytes, Type type)
        {
            if (TypeLookup.TryGetValue(type.FullName!, out var parser))
            {
                return parser.ParseFrom(bytes);
            }
            var msg = Activator.CreateInstance(type) as IMessage;
            if (msg == null)
                throw new ArgumentException($"Can't deserialize a non-protobuf message using protobuf [{type.TypeQualifiedName()}]");
            parser = msg.Descriptor.Parser;
            TypeLookup.TryAdd(type.FullName!, parser);
            return parser.ParseFrom(bytes);
        }
    }
}
