﻿//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubMessageSerializer.cs" company="Akka.NET Project">
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
using Akka.Cluster.PubSub.Serializers.Proto;
using Akka.Cluster.Tools.PublishSubscribe.Internal;
using Akka.Serialization;
using Akka.Util;
using Google.ProtocolBuffers;
using Address = Akka.Cluster.PubSub.Serializers.Proto.Address;
using Delta = Akka.Cluster.Tools.PublishSubscribe.Internal.Delta;
using Status = Akka.Cluster.PubSub.Serializers.Proto.Status;

namespace Akka.Cluster.Tools.PublishSubscribe.Serialization
{
    /**
     * Protobuf serializer of DistributedPubSubMediator messages.
     */
    /// <summary>
    /// TBD
    /// </summary>
    public class DistributedPubSubMessageSerializer : SerializerWithStringManifest
    {
        private const int BufferSize = 1024 * 4;

        private const string StatusManifest = "A";
        private const string DeltaManifest = "B";
        private const string SendManifest = "C";
        private const string SendToAllManifest = "D";
        private const string PublishManifest = "E";

        private readonly IDictionary<string, Func<byte[], object>> _fromBinaryMap;

        private readonly int _identifier;

        /// <summary>
        /// Initializes a new instance of the <see cref="DistributedPubSubMessageSerializer"/> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer.</param>
        public DistributedPubSubMessageSerializer(ExtendedActorSystem system) : base(system)
        {
            _identifier = SerializerIdentifierHelper.GetSerializerIdentifierFromConfig(this.GetType(), system);
            _fromBinaryMap = new Dictionary<string, Func<byte[], object>>
            {
                {StatusManifest, StatusFromBinary},
                {DeltaManifest, DeltaFromBinary},
                {SendManifest, SendFromBinary},
                {SendToAllManifest, SendToAllFromBinary},
                {PublishManifest, PublishFromBinary}
            };
        }

        /// <summary>
        /// Completely unique value to identify this implementation of Serializer, used to optimize network traffic
        /// Values from 0 to 16 is reserved for Akka internal usage
        /// </summary>
        public override int Identifier => _identifier;

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
            if (obj is Internal.Status) return Compress(StatusToProto(obj as Internal.Status));
            if (obj is Internal.Delta) return Compress(DeltaToProto(obj as Internal.Delta));
            if (obj is Send) return SendToProto(obj as Send).ToByteArray();
            if (obj is SendToAll) return SendToAllToProto(obj as SendToAll).ToByteArray();
            if (obj is Publish) return PublishToProto(obj as Publish).ToByteArray();

            throw new ArgumentException($"Can't serialize object of type {obj.GetType()} with {GetType()}");
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
            Func<byte[], object> deserializer;
            if (_fromBinaryMap.TryGetValue(manifest, out deserializer))
            {
                return deserializer(bytes);
            }

            throw new ArgumentException($"Unimplemented deserialization of message with manifest [{manifest}] in serializer {GetType()}");
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
            if (o is Internal.Status) return StatusManifest;
            if (o is Internal.Delta) return DeltaManifest;
            if (o is Send) return SendManifest;
            if (o is SendToAll) return SendToAllManifest;
            if (o is Publish) return PublishManifest;

            throw new ArgumentException($"Serializer {this.GetType()} cannot serialize message of type {o.GetType()}");
        }

        private byte[] Compress(IMessageLite message)
        {
            using (var bos = new MemoryStream(BufferSize))
            using (var gzipStream = new GZipStream(bos, CompressionMode.Compress))
            {
                message.WriteTo(gzipStream);
                gzipStream.Dispose();
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

        private Address.Builder AddressToProto(Actor.Address address)
        {
            if (string.IsNullOrEmpty(address.Host) || !address.Port.HasValue)
                throw new ArgumentException($"Address [{address}] could not be serialized: host or port missing");

            return Address.CreateBuilder()
                .SetSystem(address.System)
                .SetHostname(address.Host)
                .SetPort((uint)address.Port.Value)
                .SetProtocol(address.Protocol);
        }

        private Actor.Address AddressFromProto(Address address)
        {
            return new Actor.Address(address.Protocol, address.System, address.Hostname, (int)address.Port);
        }

        private Akka.Cluster.PubSub.Serializers.Proto.Delta DeltaToProto(Delta delta)
        {
            var buckets = delta.Buckets.Select(b =>
            {
                var entries = b.Content.Select(c =>
                {
                    var bb = Akka.Cluster.PubSub.Serializers.Proto.Delta.Types.Entry.CreateBuilder()
                        .SetKey(c.Key).SetVersion(c.Value.Version);
                    if (c.Value.Ref != null)
                    {
                        bb.SetRef(Akka.Serialization.Serialization.SerializedActorPath(c.Value.Ref));
                    }
                    return bb.Build();
                });
                return Akka.Cluster.PubSub.Serializers.Proto.Delta.Types.Bucket.CreateBuilder()
                    .SetOwner(AddressToProto(b.Owner))
                    .SetVersion(b.Version)
                    .AddRangeContent(entries)
                    .Build();
            }).ToArray();

            return Akka.Cluster.PubSub.Serializers.Proto.Delta.CreateBuilder()
                .AddRangeBuckets(buckets)
                .Build();
        }

        private Delta DeltaFromBinary(byte[] binary)
        {
            return DeltaFromProto(Akka.Cluster.PubSub.Serializers.Proto.Delta.ParseFrom(Decompress(binary)));
        }

        private Delta DeltaFromProto(Akka.Cluster.PubSub.Serializers.Proto.Delta delta)
        {
            return new Delta(delta.BucketsList.Select(b =>
            {
                var content = b.ContentList.Aggregate(ImmutableDictionary<string, ValueHolder>.Empty, (map, entry) =>
                     map.Add(entry.Key, new ValueHolder(entry.Version, entry.HasRef ? ResolveActorRef(entry.Ref) : null)));
                return new Bucket(AddressFromProto(b.Owner), b.Version, content);
            }).ToArray());
        }

        private IActorRef ResolveActorRef(string path)
        {
            return system.Provider.ResolveActorRef(path);
        }

        private Status StatusToProto(Internal.Status status)
        {
            var versions = status.Versions.Select(v =>
                Status.Types.Version.CreateBuilder()
                    .SetAddress(AddressToProto(v.Key))
                    .SetTimestamp(v.Value)
                    .Build())
                .ToArray();

            return Status.CreateBuilder()
                .AddRangeVersions(versions)
                .SetReplyToStatus(status.IsReplyToStatus)
                .Build();
        }

        private Internal.Status StatusFromBinary(byte[] binary)
        {
            return StatusFromProto(Status.ParseFrom(Decompress(binary)));
        }

        private Internal.Status StatusFromProto(Status status)
        {
            var isReplyToStatus = status.HasReplyToStatus ? status.ReplyToStatus : false;
            return new Internal.Status(status.VersionsList
                .ToDictionary(
                    v => AddressFromProto(v.Address),
                    v => v.Timestamp), isReplyToStatus);
        }

        private Akka.Cluster.PubSub.Serializers.Proto.Send SendToProto(Send send)
        {
            return Akka.Cluster.PubSub.Serializers.Proto.Send.CreateBuilder()
                .SetPath(send.Path)
                .SetLocalAffinity(send.LocalAffinity)
                .SetPayload(PayloadToProto(send.Message))
                .Build();
        }

        private Send SendFromBinary(byte[] binary)
        {
            return SendFromProto(Akka.Cluster.PubSub.Serializers.Proto.Send.ParseFrom(binary));
        }

        private Send SendFromProto(Akka.Cluster.PubSub.Serializers.Proto.Send send)
        {
            return new Send(send.Path, PayloadFromProto(send.Payload), send.LocalAffinity);
        }

        private Akka.Cluster.PubSub.Serializers.Proto.SendToAll SendToAllToProto(SendToAll sendToAll)
        {
            return Akka.Cluster.PubSub.Serializers.Proto.SendToAll.CreateBuilder()
                .SetPath(sendToAll.Path)
                .SetAllButSelf(sendToAll.ExcludeSelf)
                .SetPayload(PayloadToProto(sendToAll.Message))
                .Build();
        }

        private SendToAll SendToAllFromBinary(byte[] binary)
        {
            return SendToAllFromProto(Akka.Cluster.PubSub.Serializers.Proto.SendToAll.ParseFrom(binary));
        }

        private SendToAll SendToAllFromProto(Akka.Cluster.PubSub.Serializers.Proto.SendToAll send)
        {
            return new SendToAll(send.Path, PayloadFromProto(send.Payload), send.AllButSelf);
        }

        private Akka.Cluster.PubSub.Serializers.Proto.Publish PublishToProto(Publish publish)
        {
            return Akka.Cluster.PubSub.Serializers.Proto.Publish.CreateBuilder()
                .SetTopic(publish.Topic)
                .SetPayload(PayloadToProto(publish.Message))
                .Build();
        }

        private Publish PublishFromBinary(byte[] binary)
        {
            return PublishFromProto(Akka.Cluster.PubSub.Serializers.Proto.Publish.ParseFrom(binary));
        }

        private Publish PublishFromProto(Akka.Cluster.PubSub.Serializers.Proto.Publish publish)
        {
            return new Publish(publish.Topic, PayloadFromProto(publish.Payload));
        }

        private Payload PayloadToProto(object message)
        {
            var serializer = system.Serialization.FindSerializerFor(message);
            var builder = Payload.CreateBuilder()
                .SetEnclosedMessage(ByteString.CopyFrom(serializer.ToBinary(message)))
                .SetSerializerId(serializer.Identifier);

            SerializerWithStringManifest serializerWithManifest;
            if ((serializerWithManifest = serializer as SerializerWithStringManifest) != null)
            {
                var manifest = serializerWithManifest.Manifest(message);
                if (!string.IsNullOrEmpty(manifest))
                    builder.SetMessageManifest(ByteString.CopyFromUtf8(manifest));
            }
            else
            {
                if (serializer.IncludeManifest)
                    builder.SetMessageManifest(ByteString.CopyFromUtf8(message.GetType().TypeQualifiedName()));
            }

            return builder.Build();
        }

        private object PayloadFromProto(Payload payload)
        {
            var type = payload.HasMessageManifest ? Type.GetType(payload.MessageManifest.ToStringUtf8()) : null;
            return system.Serialization.Deserialize(
                payload.EnclosedMessage.ToByteArray(),
                payload.SerializerId,
                type);
        }
    }
}