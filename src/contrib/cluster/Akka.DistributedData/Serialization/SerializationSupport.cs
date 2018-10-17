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
using System.Collections.Immutable;
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

        public static VersionVector VersionVectorFromProto<T>(this T serializer, Proto.Msg.VersionVector proto) 
            where T: IWithSerializationSupport
        {
            switch (proto.Entries.Count)
            {
                case 0: return VersionVector.Empty;
                case 1:  
                    var entry = proto.Entries[0];
                    return VersionVector.Create(serializer.UniqueAddressFromProto(entry.Node), entry.Version);
                default:
                    var builder = ImmutableDictionary<UniqueAddress, long>.Empty.ToBuilder();
                    foreach (var e in proto.Entries)
                    {
                        builder.Add(serializer.UniqueAddressFromProto(e.Node), e.Version);
                    }
                    return VersionVector.Create(builder.ToImmutable());
            }
        }

        public static object OtherMessageFromProto<T>(this T serializer, Proto.Msg.OtherMessage proto) 
            where T: IWithSerializationSupport
        {
            var manifest = proto.MessageManifest == null || proto.MessageManifest.IsEmpty
                ? string.Empty
                : proto.MessageManifest.ToStringUtf8();
            return serializer.Serialization.Deserialize(proto.EnclosedMessage.ToByteArray(), proto.SerializerId, manifest);
        }

        public static UniqueAddress UniqueAddressFromProto<T>(this T serializer, Proto.Msg.UniqueAddress msg)
            where T : IWithSerializationSupport
        {
            return new UniqueAddress(serializer.AddressFromProto(msg.Address.Hostname, msg.Address.Port), msg.Uid);
        }

        public static Actor.Address AddressFromProto<T>(this T serializer, string hostname, uint port)
            where T: IWithSerializationSupport
        {
            return new Actor.Address(serializer.Protocol, serializer.System.Name, hostname, (int)port);
        }
    }

    internal interface IWithSerializationSupport
    {
        string Protocol { get; }
        Akka.Serialization.Serialization Serialization { get; }
        ActorSystem System { get; }
    }
}