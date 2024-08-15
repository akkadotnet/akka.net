//-----------------------------------------------------------------------
// <copyright file="ClusterClientMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Remote.Serialization;
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

        private const string SendManifest = "AA";
        private const string SendToAllManifest = "AB";
        private const string PublishManifest = "AC";
        private const string RefreshContactsTickManifest = "AD";
        private const string HeartbeatTickManifest = "AE";
        private const string ReconnectTimeoutManifest = "AF";
        
        private static readonly byte[] EmptyBytes = Array.Empty<byte>();
        private readonly WrappedPayloadSupport _payloadSupport;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterClientMessageSerializer"/> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer.</param>
        public ClusterClientMessageSerializer(ExtendedActorSystem system) : base(system)
        {
            _payloadSupport = new WrappedPayloadSupport(system);
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
            return obj switch
            {
                ClusterReceptionist.Contacts message => ContactsToProto(message),
                ClusterReceptionist.GetContacts => EmptyBytes,
                ClusterReceptionist.Heartbeat => EmptyBytes,
                ClusterReceptionist.HeartbeatRsp => EmptyBytes,
                ClusterReceptionist.ReceptionistShutdown => EmptyBytes,

                ClusterClient.Send message => SendToProto(message),
                ClusterClient.SendToAll message => SendToAllToProto(message),
                ClusterClient.Publish message => PublishToProto(message),
                ClusterClient.RefreshContactsTick => EmptyBytes,
                ClusterClient.HeartbeatTick => EmptyBytes,
                ClusterClient.ReconnectTimeout => EmptyBytes,
                
                _ => throw new ArgumentException(
                    $"Can't serialize object of type [{obj.GetType()}] in [{nameof(ClusterClientMessageSerializer)}]")
            };
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
            return manifest switch
            {
                ContactsManifest => ContactsFromBinary(bytes),
                GetContactsManifest => ClusterReceptionist.GetContacts.Instance,
                HeartbeatManifest => ClusterReceptionist.Heartbeat.Instance,
                HeartbeatRspManifest => ClusterReceptionist.HeartbeatRsp.Instance,
                ReceptionistShutdownManifest => ClusterReceptionist.ReceptionistShutdown.Instance,
                
                SendManifest => SendFrom(bytes),
                SendToAllManifest => SendToAllFrom(bytes),
                PublishManifest => PublishFrom(bytes),
                RefreshContactsTickManifest => ClusterClient.RefreshContactsTick.Instance,
                HeartbeatTickManifest => ClusterClient.HeartbeatTick.Instance,
                ReconnectTimeoutManifest => ClusterClient.ReconnectTimeout.Instance,
                
                _ => throw new SerializationException($"Unimplemented deserialization of message with manifest [{manifest}] in serializer {nameof(ClusterClientMessageSerializer)}")
            };
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
            return o switch
            {
                ClusterReceptionist.Contacts => ContactsManifest,
                ClusterReceptionist.GetContacts => GetContactsManifest,
                ClusterReceptionist.Heartbeat => HeartbeatManifest,
                ClusterReceptionist.HeartbeatRsp => HeartbeatRspManifest,
                ClusterReceptionist.ReceptionistShutdown => ReceptionistShutdownManifest,
                
                ClusterClient.Send => SendManifest,
                ClusterClient.SendToAll => SendToAllManifest,
                ClusterClient.Publish => PublishManifest,
                ClusterClient.RefreshContactsTick => RefreshContactsTickManifest,
                ClusterClient.HeartbeatTick => HeartbeatTickManifest,
                ClusterClient.ReconnectTimeout => ReconnectTimeoutManifest,
                
                _ => throw new ArgumentException(
                    $"Can't serialize object of type [{o.GetType()}] in [{nameof(ClusterClientMessageSerializer)}]")
            };
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
        
        private byte[] SendToProto(ClusterClient.Send send)
        {
            var protoMessage = new PublishSubscribe.Serialization.Proto.Msg.Send();
            protoMessage.Path = send.Path;
            protoMessage.LocalAffinity = send.LocalAffinity;
            protoMessage.Payload = _payloadSupport.PayloadToProto(send.Message);
            return protoMessage.ToByteArray();
        }

        private ClusterClient.Send SendFrom(byte[] bytes)
        {
            var sendProto = PublishSubscribe.Serialization.Proto.Msg.Send.Parser.ParseFrom(bytes);
            return new ClusterClient.Send(sendProto.Path, _payloadSupport.PayloadFrom(sendProto.Payload), sendProto.LocalAffinity);
        }

        private byte[] SendToAllToProto(ClusterClient.SendToAll sendToAll)
        {
            var protoMessage = new PublishSubscribe.Serialization.Proto.Msg.SendToAll();
            protoMessage.Path = sendToAll.Path;
            protoMessage.AllButSelf = false;
            protoMessage.Payload = _payloadSupport.PayloadToProto(sendToAll.Message);
            return protoMessage.ToByteArray();
        }

        private ClusterClient.SendToAll SendToAllFrom(byte[] bytes)
        {
            var sendToAllProto = PublishSubscribe.Serialization.Proto.Msg.SendToAll.Parser.ParseFrom(bytes);
            return new ClusterClient.SendToAll(sendToAllProto.Path, _payloadSupport.PayloadFrom(sendToAllProto.Payload));
        }

        private byte[] PublishToProto(ClusterClient.Publish publish)
        {
            var protoMessage = new PublishSubscribe.Serialization.Proto.Msg.Publish();
            protoMessage.Topic = publish.Topic;
            protoMessage.Payload = _payloadSupport.PayloadToProto(publish.Message);
            return protoMessage.ToByteArray();
        }

        private ClusterClient.Publish PublishFrom(byte[] bytes)
        {
            var publishProto = PublishSubscribe.Serialization.Proto.Msg.Publish.Parser.ParseFrom(bytes);
            return new ClusterClient.Publish(publishProto.Topic, _payloadSupport.PayloadFrom(publishProto.Payload));
        }
        
    }
}
