//-----------------------------------------------------------------------
// <copyright file="ProtobufSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Serialization;
using Akka.Util;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Akka.Remote.Serialization
{
    /// <summary>
    /// This is a special <see cref="Serializer"/> that serializes and deserializes Google protobuf messages only.
    /// </summary>
    public class ProtobufSerializer : Serializer
    {
        private static readonly Dictionary<string, MessageParser> TypeLookup = new Dictionary<string, MessageParser>();

        /// <summary>
        /// Initializes a new instance of the <see cref="ProtobufSerializer"/> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer. </param>
        public ProtobufSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public static void RegisterFileDescriptor(FileDescriptor fd)
        {
            foreach (var msg in fd.MessageTypes)
            {
                var name = fd.Package + "." + msg.Name;
                TypeLookup.Add(name, msg.Parser);
            }
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
            MessageParser parser;
            if (TypeLookup.TryGetValue(type.FullName, out parser))
            {
                return parser.ParseFrom(bytes);
            }

            throw new ArgumentException("Need a protobuf message class to be able to serialize bytes using protobuf");
        }
    }
}
