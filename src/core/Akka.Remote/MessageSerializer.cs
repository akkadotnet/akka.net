//-----------------------------------------------------------------------
// <copyright file="MessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Serialization;
using Google.ProtocolBuffers;
using System.Reflection;

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
            return system.Serialization.Deserialize(
                messageProtocol.Message.ToByteArray(),
                messageProtocol.SerializerId,
                messageProtocol.HasMessageManifest ? messageProtocol.MessageManifest.ToStringUtf8() : null);
        }

        /// <summary>
        /// Serializes the specified message.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <param name="address"></param>
        /// <param name="message">The message.</param>
        /// <returns>SerializedMessage.</returns>
        public static SerializedMessage Serialize(ActorSystem system, Address address, object message)
        {
            Serializer serializer = system.Serialization.FindSerializerFor(message);

            SerializedMessage.Builder messageBuilder = new SerializedMessage.Builder()
                .SetMessage(ByteString.Unsafe.FromBytes(serializer.ToBinaryWithAddress(address, message)))
                .SetSerializerId(serializer.Identifier);

            var serializer2 = serializer as SerializerWithStringManifest;
            if (serializer2 != null)
            {
                var manifest = serializer2.Manifest(message);
                if (!string.IsNullOrEmpty(manifest))
                {
                    messageBuilder.SetMessageManifest(ByteString.CopyFromUtf8(manifest));
                }
            }
            else
            {
                if (serializer.IncludeManifest)
                    messageBuilder.SetMessageManifest(ByteString.CopyFromUtf8(message.GetType().GetTypeInfo().AssemblyQualifiedName));
            }

            return messageBuilder.Build();
        }
    }
}
