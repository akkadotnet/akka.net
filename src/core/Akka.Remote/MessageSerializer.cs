//-----------------------------------------------------------------------
// <copyright file="MessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    /// INTERNAL API.
    ///
    /// MessageSerializer is a helper for serializing and deserialize messages.
    /// </summary>
    internal static class MessageSerializer
    {
        /// <summary>
        /// Uses Akka Serialization for the specified ActorSystem to transform the given MessageProtocol to a message.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <param name="messageProtocol">The message protocol.</param>
        /// <returns>System.Object.</returns>
        public static object Deserialize(ExtendedActorSystem system, SerializedMessage messageProtocol)
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
        public static SerializedMessage Serialize(ExtendedActorSystem system, Address address, object message)
        {
            var serializer = system.Serialization.FindSerializerFor(message);

            var oldInfo = Akka.Serialization.Serialization.CurrentTransportInformation;
            try
            {
                if (oldInfo == null)
                    Akka.Serialization.Serialization.CurrentTransportInformation =
                        system.Provider.SerializationInformation;

                var serializedMsg = new SerializedMessage
                {
                    Message = ByteString.CopyFrom(serializer.ToBinary(message)),
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
                        serializedMsg.MessageManifest = ByteString.CopyFromUtf8(message.GetType().TypeQualifiedName());
                }

                return serializedMsg;
            }
            finally
            {
                Akka.Serialization.Serialization.CurrentTransportInformation = oldInfo;
            }
        }
    }
}
