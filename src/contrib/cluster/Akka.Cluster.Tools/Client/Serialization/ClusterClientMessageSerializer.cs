//-----------------------------------------------------------------------
// <copyright file="ClusterClientMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Akka.Actor;
using Akka.Serialization;
using Google.ProtocolBuffers;
using Contacts = Akka.Cluster.Client.Serializers.Proto.Contacts;

namespace Akka.Cluster.Tools.Client.Serialization
{
    internal class ClusterClientMessageSerializer : SerializerWithStringManifest
    {
        private const int BufferSize = 1024 * 4;

        private const string ContactsManifest = "A";
        private const string GetContactsManifest = "B";
        private const string HeartbeatManifest = "C";
        private const string HeartbeatRspManifest = "D";

        private static readonly byte[] EmptyBytes = new byte[0];
        private readonly IDictionary<string, Func<byte[], IClusterClientMessage>> _fromBinaryMap;

        public override int Identifier { get; }

        public ClusterClientMessageSerializer(ExtendedActorSystem system) : base(system)
        {
            Identifier = SerializerIdentifierHelper.GetSerializerIdentifierFromConfig(GetType(), system);
            _fromBinaryMap = new Dictionary<string, Func<byte[], IClusterClientMessage>>
            {
                {ContactsManifest, ContactsFromBinary},
                {GetContactsManifest, _ => ClusterReceptionist.GetContacts.Instance},
                {HeartbeatManifest, _ => ClusterReceptionist.Heartbeat.Instance},
                {HeartbeatRspManifest, _ => ClusterReceptionist.HeartbeatRsp.Instance}
            };
        }

        public override byte[] ToBinary(object obj)
        {
            if (obj is ClusterReceptionist.Contacts) return Compress(ContactsToProto(obj as ClusterReceptionist.Contacts));
            if (obj is ClusterReceptionist.GetContacts) return EmptyBytes;
            if (obj is ClusterReceptionist.Heartbeat) return EmptyBytes;
            if (obj is ClusterReceptionist.HeartbeatRsp) return EmptyBytes;

            throw new ArgumentException($"Can't serialize object of type [{obj.GetType()}] in [{GetType()}]");
        }

        public override object FromBinary(byte[] bytes, string manifestString)
        {
            Func<byte[], IClusterClientMessage> deserializer;
            if (_fromBinaryMap.TryGetValue(manifestString, out deserializer))
            {
                return deserializer(bytes);
            }

            throw new ArgumentException($"Unimplemented deserialization of message with manifest [{manifestString}] in serializer {GetType()}");
        }

        public override string Manifest(object o)
        {
            if (o is ClusterReceptionist.Contacts) return ContactsManifest;
            if (o is ClusterReceptionist.GetContacts) return GetContactsManifest;
            if (o is ClusterReceptionist.Heartbeat) return HeartbeatManifest;
            if (o is ClusterReceptionist.HeartbeatRsp) return HeartbeatRspManifest;

            throw new ArgumentException($"Can't serialize object of type [{o.GetType()}] in [{GetType()}]");
        }

        private byte[] Compress(IMessageLite message)
        {
            using (var bos = new MemoryStream(BufferSize))
            using (var gzipStream = new GZipStream(bos, CompressionMode.Compress))
            {
                message.WriteTo(gzipStream);
                gzipStream.Close();
                return bos.ToArray();
            }
        }

        private byte[] Decompress(byte[] bytes)
        {
            using (var input = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[BufferSize];
                var bytesRead = input.Read(buffer, 0, BufferSize);
                while (bytesRead > 0)
                {
                    output.Write(buffer, 0, bytesRead);
                    bytesRead = input.Read(buffer, 0, BufferSize);
                }
                return output.ToArray();
            }
        }

        private Contacts ContactsToProto(ClusterReceptionist.Contacts message)
        {
            return Contacts.CreateBuilder()
                .AddRangeContactPoints(message.ContactPoints)
                .Build();
        }

        private ClusterReceptionist.Contacts ContactsFromBinary(byte[] binary)
        {
            var proto = Contacts.ParseFrom(Decompress(binary));
            return new ClusterReceptionist.Contacts(proto.ContactPointsList.ToImmutableList());
        }
    }
}