//-----------------------------------------------------------------------
// <copyright file="MessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Serialization;
using Google.ProtocolBuffers;

namespace Akka.Remote
{
    /// <summary>
    /// Class MessageSerializer.
    /// </summary>
    public static class MessageSerializer
    {
        /// <summary>
        /// Deserializes the specified message.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <param name="messageProtocol">The message protocol.</param>
        /// <returns>System.Object.</returns>
        public static object Deserialize(ActorSystem system, SerializedMessage messageProtocol)
        {
            Type type = messageProtocol.HasMessageManifest
                ? Type.GetType(messageProtocol.MessageManifest.ToStringUtf8())
                : null;
            var message = system.Serialization.Deserialize(messageProtocol.Message.ToByteArray(),
                messageProtocol.SerializerId, type);
            return message;
        }

        /// <summary>
        /// Serializes the specified message.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <param name="address"></param>
        /// <param name="message">The message.</param>
        /// <returns>SerializedMessage.</returns>
        public static SerializedMessage Serialize(ActorSystem system,Address address, object message)
        {
            Serializer serializer = system.Serialization.FindSerializerFor(message);
            byte[] messageBytes = serializer.ToBinaryWithAddress(address,message);
            SerializedMessage.Builder messageBuilder = new SerializedMessage.Builder()
                .SetSerializerId(serializer.Identifier);
            if (serializer.IncludeManifest)
                messageBuilder.SetMessageManifest(ByteString.CopyFromUtf8(message.GetType().AssemblyQualifiedName));
            messageBuilder.SetMessage(ByteString.Unsafe.FromBytes(messageBytes));

            return messageBuilder.Build();
        }
    }
}

