using Google.ProtocolBuffers;
using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Remote
{
    public static class MessageSerializer
    {
        public static object Deserialize(ActorSystem system,SerializedMessage messageProtocol)
        {
            var type = messageProtocol.HasMessageManifest ? Type.GetType(messageProtocol.MessageManifest.ToStringUtf8()) : null;
            var message = system.Serialization.Deserialize(messageProtocol.Message.ToByteArray(), messageProtocol.SerializerId, type);
            return message;
        }

        public static SerializedMessage Serialize(ActorSystem system,object message)
        {
            var serializer = system.Serialization.FindSerializerFor(message);
            var messageBytes = serializer.ToBinary(message);
            var messageBuilder = new SerializedMessage.Builder()
                .SetSerializerId(serializer.Identifier);
            if (serializer.IncludeManifest)
                messageBuilder.SetMessageManifest(ByteString.CopyFromUtf8(message.GetType().AssemblyQualifiedName));
            messageBuilder.SetMessage(ByteString.Unsafe.FromBytes(messageBytes));

            return messageBuilder.Build();
        }
    }
}
