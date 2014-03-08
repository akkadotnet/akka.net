using System;
using Akka.Actor;
using Akka.Serialization;
using Google.ProtocolBuffers;

namespace Akka.Remote
{
    public static class MessageSerializer
    {
        public static object Deserialize(ActorSystem system, SerializedMessage messageProtocol)
        {
            Type type = messageProtocol.HasMessageManifest
                ? Type.GetType(messageProtocol.MessageManifest.ToStringUtf8())
                : null;
            object message = system.Serialization.Deserialize(messageProtocol.Message.ToByteArray(),
                messageProtocol.SerializerId, type);
            return message;
        }

        public static SerializedMessage Serialize(ActorSystem system, object message)
        {
            Serializer serializer = system.Serialization.FindSerializerFor(message);
            byte[] messageBytes = serializer.ToBinary(message);
            SerializedMessage.Builder messageBuilder = new SerializedMessage.Builder()
                .SetSerializerId(serializer.Identifier);
            if (serializer.IncludeManifest)
                messageBuilder.SetMessageManifest(ByteString.CopyFromUtf8(message.GetType().AssemblyQualifiedName));
            messageBuilder.SetMessage(ByteString.Unsafe.FromBytes(messageBytes));

            return messageBuilder.Build();
        }
    }
}