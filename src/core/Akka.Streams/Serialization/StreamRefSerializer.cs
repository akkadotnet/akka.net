//-----------------------------------------------------------------------
// <copyright file="StreamRefSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Serialization;
using Akka.Streams.Implementation;
using Akka.Streams.Serialization.Proto.Msg;
using Akka.Util;
using Google.Protobuf;
using CumulativeDemand = Akka.Streams.Implementation.CumulativeDemand;
using OnSubscribeHandshake = Akka.Streams.Implementation.OnSubscribeHandshake;
using RemoteStreamCompleted = Akka.Streams.Implementation.RemoteStreamCompleted;
using RemoteStreamFailure = Akka.Streams.Implementation.RemoteStreamFailure;
using SequencedOnNext = Akka.Streams.Implementation.SequencedOnNext;

namespace Akka.Streams.Serialization
{
    public sealed class StreamRefSerializer : SerializerWithStringManifest
    {
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
                case ISourceRefImpl _: return SourceRefManifest;
                case ISinkRefImpl _: return SinkRefManifest;
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
                case ISourceRefImpl sourceRef: return SerializeSourceRef(sourceRef).ToByteArray();
                case ISinkRefImpl sinkRef: return SerializeSinkRef(sinkRef).ToByteArray();
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

        private ISinkRefImpl DeserializeSinkRef(byte[] bytes)
        {
            var sinkRef = SinkRef.Parser.ParseFrom(bytes);
            throw new NotImplementedException();
        }

        private ISourceRefImpl DeserializeSourceRef(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private RemoteStreamCompleted DeserializeRemoteSinkCompleted(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private RemoteStreamFailure DeserializeRemoteSinkFailure(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private OnSubscribeHandshake DeserializeOnSubscribeHandshake(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private CumulativeDemand DeserializeCumulativeDemand(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private SequencedOnNext DeserializeSequenceOnNext(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private ByteString SerializeSinkRef(ISinkRefImpl sinkRef) => new SinkRef
        {
            TargetRef = new ActorRef
            {
                Path = Akka.Serialization.Serialization.SerializedActorPath(sinkRef.InitialPartnerRef)
            }
        }.ToByteString();

        private ByteString SerializeSourceRef(ISourceRefImpl sourceRef)
        {
            return new SourceRef
            {
                OriginRef = new ActorRef
                {
                    Path = Akka.Serialization.Serialization.SerializedActorPath(sourceRef.InitialPartnerRef)
                }
            }.ToByteString();
        }

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
            string manifest = null;
            if (serializer.IncludeManifest)
            {
                manifest = serializer is SerializerWithStringManifest s
                    ? s.Manifest(payload)
                    : payload.GetType().TypeQualifiedName();
            }

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