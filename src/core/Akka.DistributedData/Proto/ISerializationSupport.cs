using Akka.Actor;
using Akka.Cluster;
using Akka.Serialization;
using Google.ProtocolBuffers;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using md = Akka.DistributedData.Messages;

namespace Akka.DistributedData.Proto
{
    public interface ISerializationSupport
    {
        ExtendedActorSystem System { get; }
        Serialization.Serialization Serialization { get; }
        String AddressProtocol { get; }
    }

    public static class ISerializationSupportExtensions
    {
        public static byte[] Compress(this ISerializationSupport ser, IMessageLite msg)
        {
            using(var ms = new MemoryStream())
            {
                using(var gzip = new GZipStream(ms, CompressionMode.Compress))
                {
                    msg.WriteTo(gzip);
                }
                return ms.ToArray();
            }
        }

        public static byte[] Decompress(this ISerializationSupport ser, byte[] bytes)
        {
            using(var gzipInputStream = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress))
            {
                using(var os = new MemoryStream())
                {
                    gzipInputStream.CopyTo(os);
                    return os.ToArray();
                }
            }
        }

        public static md.Address.Builder AddressToProto(this ISerializationSupport self, Address address)
        {
            if(address.Host != null && address.Port != null)
            {
                return md.Address.CreateBuilder().SetHostname(address.Host).SetPort((uint)address.Port.Value);
            }
            throw new ArgumentException(String.Format("Address {0} could not be serialized. No host or port", address));
        }

        public static Address AddressFromProto(this ISerializationSupport self, md.Address address)
        {
            var host = address.Hostname;
            var port = new Nullable<int>((int)address.Port);
            return new Address(self.AddressProtocol, self.System.Name, host, port);
        }

        public static md.UniqueAddress.Builder UniqueAddressToProto(this ISerializationSupport self, UniqueAddress address)
        {
            return md.UniqueAddress.CreateBuilder().SetAddress(self.AddressToProto(address.Address)).SetUid(address.Uid);
        }

        public static UniqueAddress UniqueAddressFromProto(this ISerializationSupport self, md.UniqueAddress address)
        {
            return new UniqueAddress(self.AddressFromProto(address.Address), address.Uid);
        }

        public static IActorRef ResolveActorRef(this ISerializationSupport self, String path)
        {
            return self.System.Provider.ResolveActorRef(path);
        }

        public static md.OtherMessage OtherMessageToProto(this ISerializationSupport self, Object msg)
        {
            var serializer = self.Serialization.FindSerializerFor(msg);
            var builder = md.OtherMessage.CreateBuilder()
                                         .SetEnclosedMessage(ByteString.CopyFrom(serializer.ToBinary(msg)))
                                         .SetSerializerId(serializer.Identifier);
            if (serializer is SerializerWithStringManifest)
            {
                var ser2 = (SerializerWithStringManifest)serializer;
                var manifest = ser2.Manifest(msg);
                if (manifest != "")
                {
                    builder.SetMessageManifest(ByteString.CopyFromUtf8(manifest));
                }
            }
            else
            {
                if (serializer.IncludeManifest)
                {
                    builder.SetMessageManifest(ByteString.CopyFromUtf8(msg.GetType().Name));
                }
            }
            return builder.Build();
        }

        public static Object OtherMessageFromBinary(this ISerializationSupport self, byte[] bytes)
        {
            return self.OtherMessageFromProto(md.OtherMessage.ParseFrom(bytes));
        }

        public static Object OtherMessageFromProto(this ISerializationSupport self, md.OtherMessage msg)
        {
            var manifest = msg.HasMessageManifest ? msg.MessageManifest.ToStringUtf8() : "";
            return self.Serialization.Deserialize(msg.EnclosedMessage.ToByteArray(), msg.SerializerId, manifest);
        }
    }
}
