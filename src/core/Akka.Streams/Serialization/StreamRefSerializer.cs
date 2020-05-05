//-----------------------------------------------------------------------
// <copyright file="StreamRefSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Text;
using Akka.Actor;
using Akka.Serialization;
using Akka.Streams.Serialization.Proto.Msg;
using Akka.Util;
using Google.Protobuf;
using Akka.Streams.Implementation.StreamRef;
using CumulativeDemand = Akka.Streams.Implementation.StreamRef.CumulativeDemand;
using OnSubscribeHandshake = Akka.Streams.Implementation.StreamRef.OnSubscribeHandshake;
using RemoteStreamCompleted = Akka.Streams.Implementation.StreamRef.RemoteStreamCompleted;
using RemoteStreamFailure = Akka.Streams.Implementation.StreamRef.RemoteStreamFailure;
using SequencedOnNext = Akka.Streams.Implementation.StreamRef.SequencedOnNext;

namespace Akka.Streams.Serialization
{
    public sealed class StreamRefSerializer : SerializerWithStringManifest
    {
        private readonly ExtendedActorSystem _system;
        private readonly Akka.Serialization.Serialization _serialization;

        private const string SequencedOnNextManifest = "A";
        private const string CumulativeDemandManifest = "B";
        private const string RemoteSinkFailureManifest = "C";
        private const string RemoteSinkCompletedManifest = "D";
        private const string SourceRefManifest = "E";
        private const string SinkRefManifest = "F";
        private const string OnSubscribeHandshakeManifest = "G";

        public StreamRefSerializer(ExtendedActorSystem system) : base(system)
        {
            _system = system;
            _serialization = system.Serialization;
        }

        public override string Manifest(object o)
        {
            switch (o)
            {
                case SequencedOnNext _: return SequencedOnNextManifest;
                case CumulativeDemand _: return CumulativeDemandManifest;
                case OnSubscribeHandshake _: return OnSubscribeHandshakeManifest;
                case RemoteStreamFailure _: return RemoteSinkFailureManifest;
                case RemoteStreamCompleted _: return RemoteSinkCompletedManifest;
                case SourceRefImpl _: return SourceRefManifest;
                case SinkRefImpl _: return SinkRefManifest;
                default: throw new ArgumentException($"Unsupported object of type {o.GetType()}", nameof(o));
            }
        }

        public override byte[] ToBinary(object o)
        {
            switch (o)
            {
                case SequencedOnNext onNext: return SerializeSequencedOnNext(onNext).ToByteArray();
                case CumulativeDemand demand: return SerializeCumulativeDemand(demand).ToByteArray();
                case OnSubscribeHandshake handshake: return SerializeOnSubscribeHandshake(handshake).ToByteArray();
                case RemoteStreamFailure failure: return SerializeRemoteStreamFailure(failure).ToByteArray();
                case RemoteStreamCompleted completed: return SerializeRemoteStreamCompleted(completed).ToByteArray();
                case SourceRefImpl sourceRef: return SerializeSourceRef(sourceRef).ToByteArray();
                case SinkRefImpl sinkRef: return SerializeSinkRef(sinkRef).ToByteArray();
                default: throw new ArgumentException($"Unsupported object of type {o.GetType()}", nameof(o));
            }
        }

        public override object FromBinary(byte[] bytes, string manifest)
        {
            switch (manifest)
            {
                case SequencedOnNextManifest: return DeserializeSequenceOnNext(bytes);
                case CumulativeDemandManifest: return DeserializeCumulativeDemand(bytes);
                case OnSubscribeHandshakeManifest: return DeserializeOnSubscribeHandshake(bytes);
                case RemoteSinkFailureManifest: return DeserializeRemoteSinkFailure(bytes);
                case RemoteSinkCompletedManifest: return DeserializeRemoteSinkCompleted(bytes);
                case SourceRefManifest: return DeserializeSourceRef(bytes);
                case SinkRefManifest: return DeserializeSinkRef(bytes);
                default: throw new ArgumentException($"Unsupported manifest '{manifest}'", nameof(manifest));
            }
        }

        private SinkRefImpl DeserializeSinkRef(byte[] bytes)
        {
            var sinkRef = SinkRef.Parser.ParseFrom(bytes);
            var type = SerializationTools.TypeFromProto(sinkRef.EventType);
            var targetRef = _system.Provider.ResolveActorRef(sinkRef.TargetRef.Path);
            return SinkRefImpl.Create(type, targetRef);
        }

        private SourceRefImpl DeserializeSourceRef(byte[] bytes)
        {
            var sourceRef = SourceRef.Parser.ParseFrom(bytes);
            return SerializationTools.ToSourceRefImpl(_system, sourceRef.EventType.TypeName, sourceRef.OriginRef.Path);
        }

        private RemoteStreamCompleted DeserializeRemoteSinkCompleted(byte[] bytes)
        {
            var completed = Proto.Msg.RemoteStreamCompleted.Parser.ParseFrom(bytes);
            return new RemoteStreamCompleted(completed.SeqNr);
        }

        private RemoteStreamFailure DeserializeRemoteSinkFailure(byte[] bytes)
        {
            var failure = Proto.Msg.RemoteStreamFailure.Parser.ParseFrom(bytes);
            var errorMessage = Encoding.UTF8.GetString(failure.Cause.ToByteArray());
            return new RemoteStreamFailure(errorMessage);
        }

        private OnSubscribeHandshake DeserializeOnSubscribeHandshake(byte[] bytes)
        {
            var handshake = Proto.Msg.OnSubscribeHandshake.Parser.ParseFrom(bytes);
            var targetRef = _system.Provider.ResolveActorRef(handshake.TargetRef.Path);
            return new OnSubscribeHandshake(targetRef);
        }

        private CumulativeDemand DeserializeCumulativeDemand(byte[] bytes)
        {
            var demand = Proto.Msg.CumulativeDemand.Parser.ParseFrom(bytes);
            return new CumulativeDemand(demand.SeqNr);
        }

        private SequencedOnNext DeserializeSequenceOnNext(byte[] bytes)
        {
            var onNext = Proto.Msg.SequencedOnNext.Parser.ParseFrom(bytes);
            var p = onNext.Payload;
            var payload = _serialization.Deserialize(
                p.EnclosedMessage.ToByteArray(), 
                p.SerializerId, 
                p.MessageManifest?.ToStringUtf8());
            return new SequencedOnNext(onNext.SeqNr, payload);
        }

        private ByteString SerializeSinkRef(SinkRefImpl sinkRef) => new SinkRef
        {
            EventType = SerializationTools.TypeToProto(sinkRef.EventType),
            TargetRef = new ActorRef
            {
                Path = Akka.Serialization.Serialization.SerializedActorPath(sinkRef.InitialPartnerRef)
            }
        }.ToByteString();

        private ByteString SerializeSourceRef(SourceRefImpl sourceRef) =>
            SerializationTools.ToSourceRef(sourceRef).ToByteString();

        private ByteString SerializeRemoteStreamCompleted(RemoteStreamCompleted completed) =>
            new Proto.Msg.RemoteStreamCompleted { SeqNr = completed.SeqNr }.ToByteString();

        private ByteString SerializeRemoteStreamFailure(RemoteStreamFailure failure) => new Proto.Msg.RemoteStreamFailure
        {
            Cause = ByteString.CopyFromUtf8(failure.Message)
        }.ToByteString();

        private ByteString SerializeOnSubscribeHandshake(OnSubscribeHandshake handshake) =>
            new Proto.Msg.OnSubscribeHandshake
            {
                TargetRef = new ActorRef
                { Path = Akka.Serialization.Serialization.SerializedActorPath(handshake.TargetRef) }
            }.ToByteString();

        private ByteString SerializeCumulativeDemand(CumulativeDemand demand) =>
            new Proto.Msg.CumulativeDemand { SeqNr = demand.SeqNr }.ToByteString();

        private ByteString SerializeSequencedOnNext(SequencedOnNext onNext)
        {
            var payload = onNext.Payload;
            var serializer = _serialization.FindSerializerFor(payload);
            var manifest = Akka.Serialization.Serialization.ManifestFor(serializer, payload);

            var p = new Payload
            {
                EnclosedMessage = ByteString.CopyFrom(serializer.ToBinary(payload)),
                SerializerId = serializer.Identifier
            };

            if (!string.IsNullOrEmpty(manifest))
                p.MessageManifest = ByteString.CopyFromUtf8(manifest);

            return new Proto.Msg.SequencedOnNext
            {
                SeqNr = onNext.SeqNr,
                Payload = p
            }.ToByteString();
        }
    }
}
