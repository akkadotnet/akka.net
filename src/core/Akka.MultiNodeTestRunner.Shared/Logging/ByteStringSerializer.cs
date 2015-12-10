using System;
using System.Linq;
using System.Net;
using System.Text;
using Akka.IO;
using Akka.Serialization;

namespace Akka.MultiNodeTestRunner.Shared.Logging
{
    /// <summary>
    /// Internal class used for byte array and message serialization
    /// inside the MultiNodeTestRunner logging system.
    /// 
    /// INTERNAL API
    /// </summary>
    public class ByteStringSerializer
    {
        private readonly Serializer _internalSerializer;

        public ByteStringSerializer(Serializer internalSerializer)
        {
            _internalSerializer = internalSerializer;
        }

        /// <summary>
        /// Append a 2-byte header to each message describing how long the FQN name is
        /// </summary>
        public const int LengthFrameLength = sizeof(int);

        

        public ByteString ToByteString(object o)
        {
            var type = o.GetType().FullName;
            var strBytes = Encoding.Unicode.GetBytes(type);
            var objBytes = _internalSerializer.ToBinary(o);
            var s = new ByteStringBuilder();
            s.PutBytes(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(strBytes.Length)));
            s.PutBytes(strBytes);
            s.PutBytes(objBytes);

            return s.Result();
        }


        public object FromByteString(ByteString bytes)
        {
            var buffer = bytes.ToArray();
            var fqnLengthHeader = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(buffer, 0));
            var fqn = Encoding.Unicode.GetString(buffer, LengthFrameLength, fqnLengthHeader);
            var type = Type.GetType(fqn, true);
            var obj = _internalSerializer.FromBinary(bytes.Skip(LengthFrameLength + fqnLengthHeader).ToArray(), type);
            return obj;
        }
    }
}
