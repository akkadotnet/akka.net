//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe.Internal;
using Akka.Remote.Serialization;
using Akka.Serialization;
using Akka.Util;
using Google.Protobuf;
using AddressData = Akka.Remote.Serialization.Proto.Msg.AddressData;
using Status = Akka.Cluster.Tools.PublishSubscribe.Internal.Status;

namespace Akka.Cluster.Tools.PublishSubscribe.Serialization
{
    /// <summary>
    /// Protobuf serializer of DistributedPubSubMediator messages.
    /// </summary>
    public class DistributedPubSubMessageSerializer : SerializerWithStringManifest
    {
        private const string StatusManifest = "A";
        private const string DeltaManifest = "B";
        private const string SendManifest = "C";
        private const string SendToAllManifest = "D";
        private const string PublishManifest = "E";
        private const string SendToOneSubscriberManifest = "F";

        private readonly IDictionary<string, Func<byte[], object>> _fromBinaryMap;

        private readonly WrappedPayloadSupport _payloadSupport;

        /// <summary>
        /// Initializes a new instance of the <see cref="DistributedPubSubMessageSerializer"/> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer.</param>
        public DistributedPubSubMessageSerializer(ExtendedActorSystem system) : base(system)
        {
            _payloadSupport = new WrappedPayloadSupport(system);
            _fromBinaryMap = new Dictionary<string, Func<byte[], object>>
            {
                {StatusManifest, StatusFrom},
                {DeltaManifest, DeltaFrom},
                {SendManifest, SendFrom},
                {SendToAllManifest, SendToAllFrom},
                {PublishManifest, PublishFrom},
                {SendToOneSubscriberManifest, SendToOneSubscriberFrom}
            };
        }

        /// <summary>
        /// Serializes the given object into a byte array
        /// </summary>
        /// <param name="obj">The object to serialize</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="obj"/> is of an unknown type.
        /// Acceptable types include:
        /// <see cref="Akka.Cluster.Tools.PublishSubscribe.Internal.Status"/> | <see cref="Akka.Cluster.Tools.PublishSubscribe.Internal.Delta"/> | <see cref="Send"/> | <see cref="SendToAll"/> | <see cref="Publish"/>
        /// </exception>
        /// <returns>A byte array containing the serialized object</returns>
        public override byte[] ToBinary(object obj)
        {
            switch (obj)
            {
                case Status status:
                    return StatusToProto(status);
                case Delta delta:
                    return DeltaToProto(delta);
                case Send send:
                    return SendToProto(send);
                case SendToAll all:
                    return SendToAllToProto(all);
                case Publish publish:
                    return PublishToProto(publish);
                case SendToOneSubscriber subscriber:
                    return SendToOneSubscriberToProto(subscriber);
                default:
                    throw new ArgumentException($"Can't serialize object of type {obj.GetType()} with {nameof(DistributedPubSubMessageSerializer)}");
            }
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

            throw new SerializationException($"Unimplemented deserialization of message with manifest [{manifest}] in serializer {nameof(DistributedPubSubMessageSerializer)}");
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
            switch (o)
            {
                case Status _:
                    return StatusManifest;
                case Delta _:
                    return DeltaManifest;
                case Send _:
                    return SendManifest;
                case SendToAll _:
                    return SendToAllManifest;
                case Publish _:
                    return PublishManifest;
                case SendToOneSubscriber _:
                    return SendToOneSubscriberManifest;
                default:
                    throw new ArgumentException($"Serializer {nameof(DistributedPubSubMessageSerializer)} cannot serialize message of type {o.GetType()}");
            }
        }

        private byte[] StatusToProto(Internal.Status status)
        {
            var message = new Proto.Msg.Status();
            message.ReplyToStatus = status.IsReplyToStatus;
            foreach (var version in status.Versions)
            {
                var protoVersion = new Proto.Msg.Status.Types.Version();
                protoVersion.Timestamp = version.Value;
                protoVersion.Address = AddressToProto(version.Key);
                message.Versions.Add(protoVersion);
            }

            return message.ToByteArray();
        }

        private Internal.Status StatusFrom(byte[] bytes)
        {
            var statusProto = Proto.Msg.Status.Parser.ParseFrom(bytes);
            var versions = new Dictionary<Address, long>();

            foreach (var protoVersion in statusProto.Versions)
            {
                versions.Add(AddressFrom(protoVersion.Address), protoVersion.Timestamp);
            }

            return new Internal.Status(versions, statusProto.ReplyToStatus);
        }

        private static byte[] DeltaToProto(Delta delta)
        {
            var message = new Proto.Msg.Delta();
            foreach (var bucket in delta.Buckets)
            {
                var protoBucket = new Proto.Msg.Delta.Types.Bucket();
                protoBucket.Owner = AddressToProto(bucket.Owner);
                protoBucket.Version = bucket.Version;

                foreach (var bucketContent in bucket.Content)
                {
                    var valueHolder = new Proto.Msg.Delta.Types.ValueHolder();
                    valueHolder.Ref = Akka.Serialization.Serialization.SerializedActorPath(bucketContent.Value.Ref); // TODO: reuse the method from the core serializer
                    valueHolder.Version = bucketContent.Value.Version;
                    protoBucket.Content.Add(bucketContent.Key, valueHolder);
                }

                message.Buckets.Add(protoBucket);
            }

            return message.ToByteArray();
        }

        private Delta DeltaFrom(byte[] bytes)
        {
            var deltaProto = Proto.Msg.Delta.Parser.ParseFrom(bytes);
            var buckets = new List<Bucket>();
            foreach (var protoBuckets in deltaProto.Buckets)
            {
                var content = new Dictionary<string, ValueHolder>();

                foreach (var protoBucketContent in protoBuckets.Content)
                {
                    var valueHolder = new ValueHolder(protoBucketContent.Value.Version, ResolveActorRef(protoBucketContent.Value.Ref));
                    content.Add(protoBucketContent.Key, valueHolder);
                }

                var bucket = new Bucket(AddressFrom(protoBuckets.Owner), protoBuckets.Version, content.ToImmutableDictionary());
                buckets.Add(bucket);
            }

            return new Delta(buckets.ToArray());
        }

        private byte[] SendToProto(Send send)
        {
            var protoMessage = new Proto.Msg.Send();
            protoMessage.Path = send.Path;
            protoMessage.LocalAffinity = send.LocalAffinity;
            protoMessage.Payload = _payloadSupport.PayloadToProto(send.Message);
            return protoMessage.ToByteArray();
        }

        private Send SendFrom(byte[] bytes)
        {
            var sendProto = Proto.Msg.Send.Parser.ParseFrom(bytes);
            return new Send(sendProto.Path, _payloadSupport.PayloadFrom(sendProto.Payload), sendProto.LocalAffinity);
        }

        private byte[] SendToAllToProto(SendToAll sendToAll)
        {
            var protoMessage = new Proto.Msg.SendToAll();
            protoMessage.Path = sendToAll.Path;
            protoMessage.AllButSelf = sendToAll.ExcludeSelf;
            protoMessage.Payload = _payloadSupport.PayloadToProto(sendToAll.Message);
            return protoMessage.ToByteArray();
        }

        private SendToAll SendToAllFrom(byte[] bytes)
        {
            var sendToAllProto = Proto.Msg.SendToAll.Parser.ParseFrom(bytes);
            return new SendToAll(sendToAllProto.Path, _payloadSupport.PayloadFrom(sendToAllProto.Payload), sendToAllProto.AllButSelf);
        }

        private byte[] PublishToProto(Publish publish)
        {
            var protoMessage = new Proto.Msg.Publish();
            protoMessage.Topic = publish.Topic;
            protoMessage.Payload = _payloadSupport.PayloadToProto(publish.Message);
            return protoMessage.ToByteArray();
        }

        private Publish PublishFrom(byte[] bytes)
        {
            var publishProto = Proto.Msg.Publish.Parser.ParseFrom(bytes);
            return new Publish(publishProto.Topic, _payloadSupport.PayloadFrom(publishProto.Payload));
        }

        private byte[] SendToOneSubscriberToProto(SendToOneSubscriber sendToOneSubscriber)
        {
            var protoMessage = new Proto.Msg.SendToOneSubscriber();
            protoMessage.Payload = _payloadSupport.PayloadToProto(sendToOneSubscriber.Message);
            return protoMessage.ToByteArray();
        }

        private SendToOneSubscriber SendToOneSubscriberFrom(byte[] bytes)
        {
            var sendToOneSubscriberProto = Proto.Msg.SendToOneSubscriber.Parser.ParseFrom(bytes);
            return new SendToOneSubscriber(_payloadSupport.PayloadFrom(sendToOneSubscriberProto.Payload));
        }

        //
        // Address
        //

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static AddressData AddressToProto(Address address)
        {
            var message = new AddressData();
            message.System = address.System;
            message.Hostname = address.Host;
            message.Port = (uint)(address.Port ?? 0);
            message.Protocol = address.Protocol;
            return message;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Address AddressFrom(AddressData addressProto)
        {
            return new Address(
                addressProto.Protocol,
                addressProto.System,
                addressProto.Hostname,
                addressProto.Port == 0 ? null : (int?)addressProto.Port);
        }

        private IActorRef ResolveActorRef(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            return system.Provider.ResolveActorRef(path);
        }


    }
}
