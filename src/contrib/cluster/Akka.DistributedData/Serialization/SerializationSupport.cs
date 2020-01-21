using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Akka.Actor;
using Akka.Serialization;
using Google.Protobuf;
using MemoryStream = System.IO.MemoryStream;

namespace Akka.DistributedData.Serialization
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Used to support the DData serializers.
    /// </summary>
    internal sealed class SerializationSupport
    {
        public SerializationSupport(ExtendedActorSystem system)
        {
            System = system;
        }

        private const int BufferSize = 1024 * 4;

        public ExtendedActorSystem System { get; }

        private volatile Akka.Serialization.Serialization _ser;

        public Akka.Serialization.Serialization Serialization
        {
            get
            {
                if (_ser == null)
                {
                    _ser = new Akka.Serialization.Serialization(System);
                }

                return _ser;
            }
        }

        private volatile string _protocol;

        public string AddressProtocol
        {
            get
            {
                if (_protocol == null)
                    _protocol = System.Provider.DefaultAddress.Protocol;
                return _protocol;
            }
        }

        private volatile Information _transportInfo;

        public Information TransportInfo
        {
            get
            {
                if (_transportInfo == null)
                {
                    var address = System.Provider.DefaultAddress;
                    _transportInfo = new Information(address, System);
                }

                return _transportInfo;
            }
        }

        public static byte[] Compress(IMessage msg)
        {
            using(var memStream = new MemoryStream())
            using (var gzip = new GZipStream(memStream, CompressionLevel.Fastest, false))
            {
                msg.WriteTo(gzip);
                return memStream.ToArray();
            }
        }

        public static byte[] Decompress(byte[] input)
        {
            using(var gzipStream = new GZipStream(new MemoryStream(input), CompressionLevel.Fastest))
            using (var memStream = new MemoryStream())
            {
                var buf = new byte[BufferSize];
                while (gzipStream.CanRead)
                {
                    var read = gzipStream.Read(buf, 0, BufferSize);
                    if (read > 0)
                    {
                        memStream.Write(buf, 0, read);
                    }
                    else
                    {
                        break;
                    }
                }

                return memStream.ToArray();
            }
        }

        public static Proto.Msg.Address AddressToProto(Address address)
        {
            if(string.IsNullOrEmpty(address.Host) || !address.Port.HasValue)
                throw new ArgumentOutOfRangeException($"Address [{address}] could not be serialized: host or port missing.");

            return new Proto.Msg.Address(){ Hostname = address.Host, Port = (uint)address.Port};
        }

        public Address AddressFromProto(Proto.Msg.Address address)
        {
            return new Address(AddressProtocol, System.Name, address.Hostname, (int)address.Port);
        }
    }
}
