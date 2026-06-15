//-----------------------------------------------------------------------
// <copyright file="MessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
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
        public static object Deserialize(ExtendedActorSystem system,
            SerializedMessage messageProtocol)
        {
            var manifest = !messageProtocol.MessageManifest.IsEmpty ? messageProtocol.MessageManifest.ToStringUtf8() : null;
            return system.Serialization.Deserialize(
                new ReadOnlySequence<byte>(messageProtocol.Message.Memory),
                messageProtocol.SerializerId,
                manifest);
        }

        /// <summary>
        /// Serializes the specified message.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <param name="transportInformation">The address for the current transport</param>
        /// <param name="message">The message.</param>
        /// <returns>SerializedMessage.</returns>
        public static SerializedMessage Serialize(ExtendedActorSystem system, Information transportInformation,
            object message)
        {
            var oldInfo = Akka.Serialization.Serialization.CurrentTransportInformation;
            try
            {
                Akka.Serialization.Serialization.CurrentTransportInformation = transportInformation;

                var serializer = system.Serialization.FindSerializerV2For(message);
                var writer = CreateWriter(serializer, message);
                serializer.Serialize(message, writer);

                var serializedMsg = new SerializedMessage
                {
                    Message = ByteString.CopyFrom(writer.WrittenSpan),
                    SerializerId = serializer.Identifier
                };

                var manifest = Akka.Serialization.Serialization.ManifestFor(serializer, message);
                if (!string.IsNullOrEmpty(manifest))
                {
                    serializedMsg.MessageManifest = ByteString.CopyFromUtf8(manifest);
                }

                return serializedMsg;
            }
            finally
            {
                Akka.Serialization.Serialization.CurrentTransportInformation = oldInfo;
            }
        }

        private static ArrayBufferWriter<byte> CreateWriter(SerializerV2 serializer, object message)
        {
            var sizeHint = serializer.SizeHint(message);
            return sizeHint > 0
                ? new ArrayBufferWriter<byte>(sizeHint)
                : new ArrayBufferWriter<byte>();
        }
    }
}
