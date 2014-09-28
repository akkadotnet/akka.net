using System.Collections.Generic;
using Helios.Buffers;
using Helios.Net;
using Google.ProtocolBuffers;

namespace Akka.Remote.TestKit.Proto
{
    /// <summary>
    /// Encodes a generic object into a <see cref="IByteBuf"/> using Google protobufs
    /// </summary>
    public class ProtobufEncoder 
    {
        public void Encode(IConnection connection, object message, out List<IByteBuf> encoded)
        {
            encoded = new List<IByteBuf>();
            var messageLite = message as IMessageLite;
            if (messageLite != null)
            {
                var buffer = connection.Allocator.Buffer();
                buffer.WriteBytes(messageLite.ToByteArray());
                encoded.Add(buffer);
                return;
            }

            var builderLite = message as IBuilderLite;
            if (builderLite != null)
            {
                var buffer = connection.Allocator.Buffer();
                buffer.WriteBytes(builderLite.WeakBuild().ToByteArray());
                encoded.Add(buffer);
            }
        }
    }
}
