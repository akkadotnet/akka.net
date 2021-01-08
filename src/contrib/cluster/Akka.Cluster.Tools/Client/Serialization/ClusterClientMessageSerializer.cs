//-----------------------------------------------------------------------
// <copyright file="ClusterClientMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Serialization;
using Google.Protobuf;

namespace Akka.Cluster.Tools.Client.Serialization
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Serializer used to translate all of the <see cref="ClusterClient"/> and <see cref="ClusterClientReceptionist"/>
    /// messages that can be passed back-and-forth between client and receptionist.
    /// </summary>
    public class ClusterClientMessageSerializer : SerializerWithStringManifest
    {
        private const string ContactsManifest = "A";
        private const string GetContactsManifest = "B";
        private const string HeartbeatManifest = "C";
        private const string HeartbeatRspManifest = "D";
        private const string ReceptionistShutdownManifest = "E";

        private static readonly byte[] EmptyBytes = {};
        private readonly IDictionary<string, Func<byte[], IClusterClientMessage>> _fromBinaryMap;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterClientMessageSerializer"/> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer.</param>
        public ClusterClientMessageSerializer(ExtendedActorSystem system) : base(system)
        {
            _fromBinaryMap = new Dictionary<string, Func<byte[], IClusterClientMessage>>
            {
                {ContactsManifest, ContactsFromBinary},
                {GetContactsManifest, _ => ClusterReceptionist.GetContacts.Instance},
                {HeartbeatManifest, _ => ClusterReceptionist.Heartbeat.Instance},
                {HeartbeatRspManifest, _ => ClusterReceptionist.HeartbeatRsp.Instance},
                {ReceptionistShutdownManifest, _ => ClusterReceptionist.ReceptionistShutdown.Instance }
            };
        }

        /// <summary>
        /// Serializes the given object into a byte array
        /// </summary>
        /// <param name="obj">The object to serialize</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="obj"/> is of an unknown type.
        /// Acceptable values include:
        /// <see cref="ClusterReceptionist.Contacts"/> | <see cref="ClusterReceptionist.GetContacts"/> | <see cref="ClusterReceptionist.Heartbeat"/> | <see cref="ClusterReceptionist.HeartbeatRsp"/>
        /// </exception>
        /// <returns>A byte array containing the serialized object</returns>
        public override byte[] ToBinary(object obj)
        {
            if (obj is ClusterReceptionist.Contacts message) return ContactsToProto(message);
            if (obj is ClusterReceptionist.GetContacts) return EmptyBytes;
            if (obj is ClusterReceptionist.Heartbeat) return EmptyBytes;
            if (obj is ClusterReceptionist.HeartbeatRsp) return EmptyBytes;
            if (obj is ClusterReceptionist.ReceptionistShutdown) return EmptyBytes;

            throw new ArgumentException($"Can't serialize object of type [{obj.GetType()}] in [{nameof(ClusterClientMessageSerializer)}]");
        }

        /// <summary>
        /// Deserializes a byte array into an object using an optional <paramref name="manifest" /> (type hint).
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <param name="manifest">The type hint used to deserialize the object contained in the array.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="bytes"/>cannot be deserialized using the specified <paramref name="manifest"/>.
        /// </exception>
        /// <returns>The object contained in the array</returns>
        public override object FromBinary(byte[] bytes, string manifest)
        {
            if (_fromBinaryMap.TryGetValue(manifest, out var deserializer))
                return deserializer(bytes);

            throw new SerializationException($"Unimplemented deserialization of message with manifest [{manifest}] in serializer {nameof(ClusterClientMessageSerializer)}");
        }

        /// <summary>
        /// Returns the manifest (type hint) that will be provided in the <see cref="FromBinary(System.Byte[],System.String)" /> method.
        /// <note>
        /// This method returns <see cref="String.Empty" /> if a manifest is not needed.
        /// </note>
        /// </summary>
        /// <param name="o">The object for which the manifest is needed.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="o"/> does not have an associated manifest.
        /// </exception>
        /// <returns>The manifest needed for the deserialization of the specified <paramref name="o" />.</returns>
        public override string Manifest(object o)
        {
            if (o is ClusterReceptionist.Contacts) return ContactsManifest;
            if (o is ClusterReceptionist.GetContacts) return GetContactsManifest;
            if (o is ClusterReceptionist.Heartbeat) return HeartbeatManifest;
            if (o is ClusterReceptionist.HeartbeatRsp) return HeartbeatRspManifest;
            if (o is ClusterReceptionist.ReceptionistShutdown) return ReceptionistShutdownManifest;

            throw new ArgumentException($"Can't serialize object of type [{o.GetType()}] in [{nameof(ClusterClientMessageSerializer)}]");
        }

        private static byte[] ContactsToProto(ClusterReceptionist.Contacts message)
        {
            var protoMessage = new Proto.Msg.Contacts();
            foreach (var contactPoint in message.ContactPoints)
            {
                protoMessage.ContactPoints.Add(contactPoint);
            }
            return protoMessage.ToByteArray();
        }

        private static ClusterReceptionist.Contacts ContactsFromBinary(byte[] binary)
        {
            var proto = Proto.Msg.Contacts.Parser.ParseFrom(binary);
            return new ClusterReceptionist.Contacts(proto.ContactPoints.ToImmutableList());
        }
    }
}
