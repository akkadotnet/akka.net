using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Serialization;

namespace Akka.Cluster.Tools.Client.Serialization
{
    public class ClusterClientMessageSerializer : SerializerWithStringManifest
    {
        private const string ContactsManifest = "A";
        private const string GetContactsManifest = "B";
        private const string HeartbeatManifest = "C";
        private const string HeartbeatRspManifest = "D";

        private static readonly byte[] EmptyBytes = new byte[0];
        private static readonly IDictionary<string, Func<byte[], IClusterClientMessage>> FromBinaryMap = new Dictionary<string, Func<byte[], IClusterClientMessage>>
        {
            {ContactsManifest, ContactsFromBinary},
            {GetContactsManifest, _ => ClusterReceptionist.GetContacts.Instance},
            {HeartbeatManifest, _ => ClusterReceptionist.Heartbeat.Instance},
            {HeartbeatRspManifest, _ => ClusterReceptionist.HeartbeatRsp.Instance}
        };

        private readonly int _identifier;
        public ClusterClientMessageSerializer(ExtendedActorSystem system) : base(system)
        {
            _identifier = SerializerIdentifierHelper.GetSerializerIdentifierFromConfig(this.GetType(), system);
        }

        public override int Identifier { get { return _identifier; } }

        public override byte[] ToBinary(object o)
        {
            if (o is ClusterReceptionist.Contacts) return ContactsToProto((ClusterReceptionist.Contacts)o).ToByteArray();
            if (o is ClusterReceptionist.GetContacts) return EmptyBytes;
            if (o is ClusterReceptionist.Heartbeat) return EmptyBytes;
            if (o is ClusterReceptionist.HeartbeatRsp) return EmptyBytes;

            throw new ArgumentException(string.Format("Can't serialize object of type [{0}] in [{1}]", o.GetType(), GetType()));
        }

        public override object FromBinary(byte[] binary, string manifest)
        {
            Func<byte[], IClusterClientMessage> mapper;
            if (FromBinaryMap.TryGetValue(manifest, out mapper))
            {
                return mapper(binary);
            }

            throw new ArgumentException(string.Format("Unimplemented deserialization of message with manifest [{0}] in [{1}]", manifest, GetType()));
        }

        public override string Manifest(object o)
        {
            if (o is ClusterReceptionist.Contacts) return ContactsManifest;
            if (o is ClusterReceptionist.GetContacts) return GetContactsManifest;
            if (o is ClusterReceptionist.Heartbeat) return HeartbeatManifest;
            if (o is ClusterReceptionist.HeartbeatRsp) return HeartbeatRspManifest;

            throw new ArgumentException(string.Format("Can't serialize object of type [{0}] in [{1}]", o.GetType(), GetType()));
        }

        private Contacts ContactsToProto(ClusterReceptionist.Contacts message)
        {
            return Contacts.CreateBuilder()
                .AddRangeContactPoints(message.ContactPoints)
                .Build();
        }

        private static ClusterReceptionist.Contacts ContactsFromBinary(byte[] binary)
        {
            var proto = Contacts.ParseFrom(binary);
            return new ClusterReceptionist.Contacts(proto.ContactPointsList.ToArray());
        }
    }
}