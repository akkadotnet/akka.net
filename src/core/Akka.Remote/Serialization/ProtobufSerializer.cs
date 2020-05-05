//-----------------------------------------------------------------------
// <copyright file="ProtobufSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
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
        private static readonly ConcurrentDictionary<string, MessageParser> TypeLookup = new ConcurrentDictionary<string, MessageParser>();

        /// <summary>
        /// Initializes a new instance of the <see cref="ProtobufSerializer"/> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer. </param>
        public ProtobufSerializer(ExtendedActorSystem system) : base(system)
        {
        }

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
        public override object FromBinary(byte[] bytes, Type type)
        {
            if (TypeLookup.TryGetValue(type.FullName, out var parser))
            {
                return parser.ParseFrom(bytes);
            }
            // MethodParser is not in the cache, look it up with reflection
            IMessage msg = Activator.CreateInstance(type) as IMessage;
            if(msg == null) throw new ArgumentException($"Can't deserialize a non-protobuf message using protobuf [{type.TypeQualifiedName()}]");
            parser = msg.Descriptor.Parser;
            TypeLookup.TryAdd(type.FullName, parser);
            return parser.ParseFrom(bytes);
        }
    }
}
