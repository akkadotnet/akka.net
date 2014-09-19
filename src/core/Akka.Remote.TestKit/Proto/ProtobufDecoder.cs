using Google.ProtocolBuffers;
using Helios.Buffers;

namespace Akka.Remote.TestKit.Proto
{
    /// <summary>
    /// Decodes a message from a <see cref="IByteBuf"/> into a Google protobuff wire format
    /// </summary>
    public class ProtobufDecoder
    {
        private readonly IMessageLite _prototype;
        private readonly ExtensionRegistry _extensions;

        public ProtobufDecoder(IMessageLite prototype)
            : this(prototype, null)
        {
        }

        public ProtobufDecoder(IMessageLite prototype, ExtensionRegistry extensions)
        {
            _prototype = prototype;
            _extensions = extensions;
        }

        public object Decode(byte[] buffer)
        {
            var byteString = ByteString.CopyFrom(buffer);
            return _extensions == null
                 ? _prototype.WeakToBuilder().WeakMergeFrom(byteString).WeakBuild()
                 : _prototype.WeakToBuilder().WeakMergeFrom(byteString, _extensions).WeakBuild();
        }
    }
}