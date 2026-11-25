//-----------------------------------------------------------------------
// <copyright file="ProtobufSerializerSetup.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Immutable;
using Akka.Actor.Setup;
using Google.Protobuf.Reflection;

namespace Akka.Remote.Serialization
{
    /// <summary>
    /// Registers generated Protobuf message parsers for deserialization without dynamic type loading.
    /// </summary>
    public sealed class ProtobufSerializerSetup : Setup
    {
        /// <summary>
        /// Creates a setup for the supplied message descriptors. Register each message type received by this system.
        /// </summary>
        /// <param name="messageDescriptors">Descriptors for generated Protobuf message types.</param>
        /// <returns>An immutable serializer setup.</returns>
        public static ProtobufSerializerSetup Create(params MessageDescriptor[] messageDescriptors)
        {
            if (messageDescriptors is null)
                throw new ArgumentNullException(nameof(messageDescriptors));

            foreach (var descriptor in messageDescriptors)
            {
                if (descriptor is null || descriptor.ClrType is null || descriptor.Parser is null)
                    throw new ArgumentException("Each descriptor must have a generated CLR type and parser.", nameof(messageDescriptors));
            }

            return new ProtobufSerializerSetup(messageDescriptors.ToImmutableHashSet());
        }

        /// <summary>
        /// The explicitly registered message descriptors.
        /// </summary>
        public ImmutableHashSet<MessageDescriptor> MessageDescriptors { get; }

        private ProtobufSerializerSetup(ImmutableHashSet<MessageDescriptor> messageDescriptors)
        {
            MessageDescriptors = messageDescriptors;
        }
    }
}
