//-----------------------------------------------------------------------
// <copyright file="MessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Serialization;
using Akka.Util;
using Google.Protobuf;
using SerializedMessage = Akka.Remote.Serialization.Proto.Msg.Payload;

namespace Akka.Remote
{
    /// <summary>
    /// Class MessageSerializer.
    /// </summary>
    internal static class MessageSerializer
    {
        /// <summary>
        /// Deserializes the specified message.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <param name="messageProtocol">The message protocol.</param>
        /// <returns>System.Object.</returns>
        public static object Deserialize(ActorSystem system, SerializedMessage messageProtocol)
        {
            return system.Serialization.Deserialize(
                messageProtocol.Message.ToByteArray(),
                messageProtocol.SerializerId,
                !messageProtocol.MessageManifest.IsEmpty ? messageProtocol.MessageManifest.ToStringUtf8() : null);
        }

        /// <summary>
        /// Serializes the specified message.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <param name="address">TBD</param>
        /// <param name="message">The message.</param>
        /// <returns>SerializedMessage.</returns>
        public static SerializedMessage Serialize(ActorSystem system, Address address, object message)
        {
            var objectType = message.GetType();
            Serializer serializer = system.Serialization.FindSerializerForType(objectType);

            var serializedMsg = new SerializedMessage
            {
                Message = ByteString.CopyFrom(serializer.ToBinaryWithAddress(address, message)),
                SerializerId = serializer.Identifier
            };

            if (serializer is SerializerWithStringManifest serializer2)
            {
                var manifest = serializer2.Manifest(message);
                if (!string.IsNullOrEmpty(manifest))
                {
                    serializedMsg.MessageManifest = ByteString.CopyFromUtf8(manifest);
                }
            }
            else
            {
                if (serializer.IncludeManifest)
                    serializedMsg.MessageManifest = ByteString.CopyFromUtf8(objectType.TypeQualifiedName());
            }

            return serializedMsg;
        }
    }
}
