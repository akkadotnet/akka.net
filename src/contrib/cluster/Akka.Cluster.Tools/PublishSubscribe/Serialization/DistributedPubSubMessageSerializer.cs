//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe.Internal;
using Akka.Cluster.Tools.PublishSubscribe.Serialization.Proto.Msg;
using Akka.Remote.Serialization;
using Akka.Serialization;
using Akka.Util;
using Google.Protobuf;
using Google.Protobuf.Collections;
using AddressData = Akka.Remote.Serialization.Proto.Msg.AddressData;
using Delta = Akka.Cluster.Tools.PublishSubscribe.Internal.Delta;
using GetLocalPubSubState = Akka.Cluster.Tools.PublishSubscribe.Query.GetLocalPubSubState;
using GetLocalPubSubStats = Akka.Cluster.Tools.PublishSubscribe.Query.GetLocalPubSubStats;
using GetPubSubState = Akka.Cluster.Tools.PublishSubscribe.Query.GetPubSubState;
using GetPubSubStats = Akka.Cluster.Tools.PublishSubscribe.Query.GetPubSubStats;
using LocalPubSubState = Akka.Cluster.Tools.PublishSubscribe.Query.LocalPubSubState;
using LocalPubSubStats = Akka.Cluster.Tools.PublishSubscribe.Query.LocalPubSubStats;
using PubSubState = Akka.Cluster.Tools.PublishSubscribe.Query.PubSubState;
using PubSubStats = Akka.Cluster.Tools.PublishSubscribe.Query.PubSubStats;
using SendToOneSubscriber = Akka.Cluster.Tools.PublishSubscribe.Internal.SendToOneSubscriber;
using Status = Akka.Cluster.Tools.PublishSubscribe.Internal.Status;
using TopicStats = Akka.Cluster.Tools.PublishSubscribe.Query.TopicStats;
using TopicState = Akka.Cluster.Tools.PublishSubscribe.Query.TopicState;

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
        
        private const string GetLocalPubSubStatsManifest = "qA";
        private const string GetPubSubStatsManifest = "qB";
        private const string LocalPubSubStatsManifest = "qC";
        private const string PubSubStatsManifest = "qD";
        
        private const string GetLocalPubSubStateManifest = "qR";
        private const string GetPubSubStateManifest = "qS";
        private const string LocalPubSubStateManifest = "qT";
        private const string PubSubStateManifest = "qU";

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
                {SendToOneSubscriberManifest, SendToOneSubscriberFrom},
                
                {GetLocalPubSubStatsManifest, GetLocalPubSubStatsFrom},
                {LocalPubSubStatsManifest, LocalPubSubStatsFrom},
                {GetPubSubStatsManifest, GetPubSubStatsFrom},
                {PubSubStatsManifest, PubSubStatsFrom},
                
                {GetLocalPubSubStateManifest, GetLocalPubSubStateFrom},
                {LocalPubSubStateManifest, LocalPubSubStateFrom},
                {GetPubSubStateManifest, GetPubSubStateFrom},
                {PubSubStateManifest, PubSubStateFrom},
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
                
                case GetLocalPubSubStats:
                    return GetLocalPubSubStatsToProto();
                case LocalPubSubStats localPubSubStats:
                    return LocalPubSubStatsToBytes(localPubSubStats);
                case GetPubSubStats:
                    return GetPubSubStatsToProto();
                case PubSubStats pubSubStats:
                    return PubSubStatsToBytes(pubSubStats);
                
                case GetLocalPubSubState:
                    return GetLocalPubSubStateToProto();
                case LocalPubSubState localPubSubState:
                    return LocalPubSubStateToBytes(localPubSubState);
                case GetPubSubState:
                    return GetPubSubStateToProto();
                case PubSubState pubSubState:
                    return PubSubStateToBytes(pubSubState);
                
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
                
                case GetLocalPubSubStats:
                    return GetLocalPubSubStatsManifest;
                case GetPubSubStats:
                    return GetPubSubStatsManifest;
                case LocalPubSubStats:
                    return LocalPubSubStatsManifest;
                case PubSubStats:
                    return PubSubStatsManifest;
                
                case GetLocalPubSubState:
                    return GetLocalPubSubStateManifest;
                case GetPubSubState:
                    return GetPubSubStateManifest;
                case LocalPubSubState:
                    return LocalPubSubStateManifest;
                case PubSubState:
                    return PubSubStateManifest;
                
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
            var versions = ImmutableDictionary.CreateBuilder<Address, long>();

            foreach (var protoVersion in statusProto.Versions)
            {
                versions.Add(AddressFrom(protoVersion.Address), protoVersion.Timestamp);
            }

            return new Internal.Status(versions.ToImmutable(), statusProto.ReplyToStatus);
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
            var buckets = ImmutableList.CreateBuilder<Bucket>();
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

            return new Delta(buckets.ToImmutable());
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

        #region Stats query

        private static byte[] GetLocalPubSubStatsToProto()
            => new Proto.Msg.GetLocalPubSubStats().ToByteArray();

        private static GetLocalPubSubStats GetLocalPubSubStatsFrom(byte[] bytes)
            => GetLocalPubSubStats.Instance;
        
        private static Proto.Msg.LocalPubSubStats LocalPubSubStatsToProto(LocalPubSubStats localPubSubStats)
        {
            var protoMessage = new Proto.Msg.LocalPubSubStats();
            protoMessage.Topics.Add(localPubSubStats.Topics.ToDictionary(
                t => t.Key, 
                t => new Proto.Msg.TopicStats { Name = t.Value.Name, Count = t.Value.SubscriberCount }));
            return protoMessage;
        }

        private static byte[] LocalPubSubStatsToBytes(LocalPubSubStats localPubSubStats)
            => LocalPubSubStatsToProto(localPubSubStats).ToByteArray();

        private static LocalPubSubStats LocalPubSubStatsFrom(byte[] bytes)
            => LocalPubSubStatsFrom(Proto.Msg.LocalPubSubStats.Parser.ParseFrom(bytes));
        
        private static LocalPubSubStats LocalPubSubStatsFrom(Proto.Msg.LocalPubSubStats protoMessage)
            =>  new (protoMessage.Topics
                .Select(t => new KeyValuePair<string, TopicStats>(t.Key, new TopicStats(t.Value.Name, t.Value.Count)))
                .ToImmutableDictionary());
        
        private static byte[] GetPubSubStatsToProto()
            => new Proto.Msg.GetPubSubStats().ToByteArray();

        private static GetPubSubStats GetPubSubStatsFrom(byte[] bytes)
            => GetPubSubStats.Instance;

        private static Proto.Msg.PubSubStats PubSubStatsToProto(PubSubStats pubSubStats)
        {
            var protoMessage = new Proto.Msg.PubSubStats();
            protoMessage.ClusterStats.Add(pubSubStats.ClusterStats.Select(
                t => new Proto.Msg.PubSubStats.Types.AddressLocalPubSubStatsPair {
                    Key = AddressToProto(t.Key), 
                    Value = LocalPubSubStatsToProto(t.Value)
                }));
            return protoMessage;
        }

        private static byte[] PubSubStatsToBytes(PubSubStats pubSubStats)
            => PubSubStatsToProto(pubSubStats).ToByteArray();

        private static PubSubStats PubSubStatsFrom(byte[] bytes)
        {
            var protoMessage = Proto.Msg.PubSubStats.Parser.ParseFrom(bytes);
            var dict = protoMessage.ClusterStats
                .Select(pair => new KeyValuePair<Address, LocalPubSubStats>(
                    AddressFrom(pair.Key), 
                    LocalPubSubStatsFrom(pair.Value)))
                .ToImmutableDictionary();
            
            return new PubSubStats(dict);
        }

        #endregion

        #region State query

        private static byte[] GetLocalPubSubStateToProto()
            => new Proto.Msg.GetLocalPubSubState().ToByteArray();

        private static GetLocalPubSubState GetLocalPubSubStateFrom(byte[] bytes)
            => GetLocalPubSubState.Instance;
        
        private static Proto.Msg.LocalPubSubState LocalPubSubStateToProto(LocalPubSubState localPubSubStats)
        {
            var protoMessage = new Proto.Msg.LocalPubSubState();
            protoMessage.Topics.Add(localPubSubStats.Topics.ToDictionary(
                kvp => kvp.Key, 
                kvp => {
                    var state = new Proto.Msg.TopicState { Name = kvp.Value.Name };
                    state.Subscribers.Add(kvp.Value.Subscribers.Select(AddressToProto));
                    return state;
                }));

            return protoMessage;
        }

        private static byte[] LocalPubSubStateToBytes(LocalPubSubState localPubSubState)
            => LocalPubSubStateToProto(localPubSubState).ToByteArray();

        private static LocalPubSubState LocalPubSubStateFrom(byte[] bytes)
            => LocalPubSubStateFrom(Proto.Msg.LocalPubSubState.Parser.ParseFrom(bytes));
        
        private static LocalPubSubState LocalPubSubStateFrom(Proto.Msg.LocalPubSubState protoMessage)
        {
            return new LocalPubSubState(protoMessage.Topics.Select(kvp =>
                new KeyValuePair<string, TopicState>(
                    kvp.Key, 
                    new TopicState(kvp.Value.Name, kvp.Value.Subscribers.Select(AddressFrom).ToImmutableList())))
                .ToImmutableDictionary());
        }
        
        private static byte[] GetPubSubStateToProto()
            => new Proto.Msg.GetPubSubState().ToByteArray();

        private static GetPubSubState GetPubSubStateFrom(byte[] bytes)
            => GetPubSubState.Instance;

        private static Proto.Msg.PubSubState PubSubStateToProto(PubSubState pubSubState)
        {
            var protoMessage = new Proto.Msg.PubSubState();
            protoMessage.ClusterState.Add(pubSubState.ClusterStates.Select(kvp =>
                new Proto.Msg.PubSubState.Types.AddressLocalPubSubStatePair {
                    Key = AddressToProto(kvp.Key), 
                    Value = LocalPubSubStateToProto(kvp.Value)
                }
            ));

            return protoMessage;
        }

        private static byte[] PubSubStateToBytes(PubSubState pubSubState)
            => PubSubStateToProto(pubSubState).ToByteArray();

        private static PubSubState PubSubStateFrom(byte[] bytes)
        {
            var protoMessage = Proto.Msg.PubSubState.Parser.ParseFrom(bytes);
            return new PubSubState(protoMessage.ClusterState
                .Select(kvp => new KeyValuePair<Address, LocalPubSubState>(
                    AddressFrom(kvp.Key), 
                    LocalPubSubStateFrom(kvp.Value)))
                .ToImmutableDictionary());
        }

        #endregion
        
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
