#region copyright
//-----------------------------------------------------------------------
// <copyright file="SerializationSupport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Akka.Actor;
using Akka.Cluster;
using Google.Protobuf;

namespace Akka.DistributedData.Serialization
{
    internal static class SerializationSupport
    {
        public const int BufferSize = 1024 * 4;
        
        public static byte[] Compress(this IMessage msg)
        {
            using (var stream = new MemoryStream(BufferSize))
            using (var gzip = new GZipStream(stream, CompressionMode.Compress))
            {
                msg.WriteTo(gzip);
                return stream.ToArray();
            }
        }

        public static byte[] Decompress(this byte[] bytes)
        {
            using (var input = new MemoryStream(bytes))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream(bytes))
            {
                gzip.CopyTo(output, BufferSize);
                return output.ToArray();
            }
        }

        public static Proto.Msg.Address ToProto(this Address address)
        {
            if (!address.Port.HasValue || string.IsNullOrEmpty(address.Host))
            {
                throw new ArgumentException($"Address [{address}] could not be serialized: host or port missing.");
            }

            return new Proto.Msg.Address
            {
                Hostname = address.Host,
                Port = (uint)address.Port.Value
            };
        }

        public static Proto.Msg.UniqueAddress ToProto(this UniqueAddress address)
        {
            return new Proto.Msg.UniqueAddress
            {
                Address = address.Address.ToProto(),
                Uid = address.Uid
            };
        }

        public static Proto.Msg.VersionVector ToProto(this VersionVector vvector)
        {
            var proto = new Proto.Msg.VersionVector();
            using (var e = vvector.VersionEnumerator)
            {
                while (e.MoveNext())
                {
                    var current = e.Current;
                    proto.Entries.Add(new Proto.Msg.VersionVector.Types.Entry
                    {
                        Node = current.Key.ToProto(),
                        Version = current.Value
                    });
                }
            }

            return proto;
        }

        public static VersionVector VersionVectorFromProto(Proto.Msg.VersionVector deltaVersions)
        {
            throw new NotImplementedException();
        }

        internal static UniqueAddress UniqueAddressFromProto(Proto.Msg.UniqueAddress removedAddress)
        {
            throw new NotImplementedException();
        }
    }
}