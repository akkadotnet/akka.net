//-----------------------------------------------------------------------
// <copyright file="NativePersistenceMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Persistence.Fsm;
using Akka.Persistence.Serialization.Proto.Msg;
using Akka.Serialization;
using Akka.Util;
using Google.Protobuf;

namespace Akka.Persistence.Serialization
{
    /// <summary>Same-id, same-protobuf native envelope implementation. The public legacy class stays unchanged.</summary>
    internal sealed class NativePersistenceMessageSerializer : SerializerV2
    {
        public NativePersistenceMessageSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        /// <inheritdoc />
        public override int Identifier => 7;

        // The fixed legacy manifest aliases. Generic FSM snapshots retain the guarded legacy JIT codec.
        private static readonly Dictionary<string, Type> ManifestTypes = Akka.Util.TypeExtensions.ManifestTable(
            typeof(Persistent), typeof(IPersistentRepresentation), typeof(AtomicWrite),
            typeof(AtLeastOnceDeliverySnapshot), typeof(PersistentFSM.StateChangeEvent));

        public override string Manifest(object obj)
        {
            if (IsFixedMessage(obj))
                return obj.GetType().TypeQualifiedName();
            if (IsFsmSnapshot(obj))
            {
                RequireDynamicFsm();
                return obj.GetType().TypeQualifiedName();
            }
            throw UnexpectedMessage(obj);
        }

        public override byte[] ToBinary(object obj)
        {
            if (IsFixedMessage(obj))
                return ToProto(obj).ToByteArray();
            if (!IsFsmSnapshot(obj))
                throw UnexpectedMessage(obj);
            RequireDynamicFsm();
            return new PersistenceMessageSerializer(system).ToBinary(obj);
        }

        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            if (IsFixedMessage(obj))
            {
                var message = ToProto(obj);
                var size = message.CalculateSize();
                message.WriteTo(writer);
                return size;
            }
            if (!IsFsmSnapshot(obj))
                throw UnexpectedMessage(obj);
            RequireDynamicFsm();
            // This existing JIT-only family retains its exact original implementation and bytes.
            var bytes = new PersistenceMessageSerializer(system).ToBinary(obj);
            writer.Write(bytes);
            return bytes.Length;
        }

        private Google.Protobuf.IMessage ToProto(object obj) => obj switch
        {
            IPersistentRepresentation persistent => GetPersistentMessage(persistent),
            AtomicWrite atomic => GetAtomicWrite(atomic),
            AtLeastOnceDeliverySnapshot delivery => GetAtLeastOnceDeliverySnapshot(delivery),
            PersistentFSM.StateChangeEvent state => GetStateChangeEvent(state),
            _ => throw UnexpectedMessage(obj)
        };

        private static bool IsFixedMessage(object obj) => obj is IPersistentRepresentation or AtomicWrite
            or AtLeastOnceDeliverySnapshot or PersistentFSM.StateChangeEvent;

        private static bool IsFsmSnapshot(object obj) => obj.GetType().IsGenericType &&
            obj.GetType().GetGenericTypeDefinition() == typeof(PersistentFSM.PersistentFSMSnapshot<>);

        private static void RequireDynamicFsm()
        {
            if (!AkkaFeatures.IsDynamicTypeLoadingSupported)
                throw new SerializationException("PersistentFSMSnapshot<T> requires its legacy reflection-based codec. " +
                    "It is unsupported with Akka.DynamicTypeLoading disabled; no native generic factory is registered.");
        }

        private static ArgumentException UnexpectedMessage(object obj) => new(
            $"Cannot serialize object of type [{obj.GetType()}] in [{typeof(NativePersistenceMessageSerializer)}]", nameof(obj));

        private PersistentMessage GetPersistentMessage(IPersistentRepresentation persistent)
        {
            var message = new PersistentMessage();

            if (persistent.PersistenceId != null) message.PersistenceId = persistent.PersistenceId;
            if (persistent.Manifest != null) message.Manifest = persistent.Manifest;
            if (persistent.WriterGuid != null) message.WriterGuid = persistent.WriterGuid;
            if (persistent.Sender != null) message.Sender = Akka.Serialization.Serialization.SerializedActorPath(persistent.Sender);
            if (persistent.Timestamp > 0L) message.Timestamp = persistent.Timestamp;

            message.Payload = GetPersistentPayload(persistent.Payload);
            message.SequenceNr = persistent.SequenceNr;
            message.Deleted = persistent.IsDeleted;

            return message;
        }

        private PersistentPayload GetPersistentPayload(object obj)
        {
            PersistentPayload BuildPayload()
            {
                var serializer = system.Serialization.FindSerializerFor(obj);
                var payload = new PersistentPayload();

                var manifest = Akka.Serialization.Serialization.ManifestFor(serializer, obj);
                if (!string.IsNullOrEmpty(manifest))
                {
                    payload.PayloadManifest = ByteString.CopyFromUtf8(manifest);
                }

                payload.Payload = ByteString.CopyFrom(serializer.ToBinary(obj));
                payload.SerializerId = serializer.Identifier;

                return payload;
            }

            var oldInfo = Akka.Serialization.Serialization.CurrentTransportInformation;
            try
            {
                if (oldInfo == null)
                    Akka.Serialization.Serialization.CurrentTransportInformation =
                        system.Provider.SerializationInformation;
                return BuildPayload();
            }
            finally
            {
                Akka.Serialization.Serialization.CurrentTransportInformation = oldInfo;
            }
        }

        private Proto.Msg.AtomicWrite GetAtomicWrite(AtomicWrite write)
        {
            var message = new Proto.Msg.AtomicWrite();
            foreach (var pr in (IImmutableList<IPersistentRepresentation>)write.Payload)
            {
                message.Payload.Add(GetPersistentMessage(pr));
            }
            return message;
        }

        private Proto.Msg.AtLeastOnceDeliverySnapshot GetAtLeastOnceDeliverySnapshot(AtLeastOnceDeliverySnapshot snapshot)
        {
            var message = new Proto.Msg.AtLeastOnceDeliverySnapshot
            {
                CurrentDeliveryId = snapshot.CurrentDeliveryId
            };

            foreach (var unconfirmed in snapshot.UnconfirmedDeliveries)
            {
                message.UnconfirmedDeliveries.Add(new Proto.Msg.UnconfirmedDelivery
                {
                    DeliveryId = unconfirmed.DeliveryId,
                    Destination = unconfirmed.Destination.ToString(),
                    Payload = GetPersistentPayload(unconfirmed.Message)
                });
            }
            return message;
        }

        private PersistentStateChangeEvent GetStateChangeEvent(PersistentFSM.StateChangeEvent changeEvent)
        {
            var message = new PersistentStateChangeEvent
            {
                StateIdentifier = changeEvent.StateIdentifier
            };
            if (changeEvent.Timeout.HasValue)
            {
                message.TimeoutMillis = (long)changeEvent.Timeout.Value.TotalMilliseconds;
            }
            return message;
        }

        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
        {
            if (string.IsNullOrEmpty(manifest))
                return GetPersistentRepresentation(PersistentMessage.Parser.ParseFrom(bytes));
            if (ManifestTypes.TryResolveManifestType(manifest, out var type))
            {
                if (type == typeof(Persistent) || type == typeof(IPersistentRepresentation))
                    return GetPersistentRepresentation(PersistentMessage.Parser.ParseFrom(bytes));
                if (type == typeof(AtomicWrite)) return GetAtomicWrite(bytes);
                if (type == typeof(AtLeastOnceDeliverySnapshot)) return GetAtLeastOnceDeliverySnapshot(bytes);
                if (type == typeof(PersistentFSM.StateChangeEvent)) return GetStateChangeEvent(bytes);
            }
            if (!AkkaFeatures.IsDynamicTypeLoadingSupported)
                throw new SerializationException($"Cannot resolve persistence manifest [{manifest}] with Akka.DynamicTypeLoading disabled. " +
                    "Generic PersistentFSMSnapshot<T> requires the legacy JIT codec and has no native factory.");
            return new PersistenceMessageSerializer(system).FromBinary(bytes.ToArray(), manifest);
        }

        private IPersistentRepresentation GetPersistentRepresentation(PersistentMessage message)
        {
            IActorRef? sender = ActorRefs.NoSender;
            if (message.Sender != null)
            {
                sender = system.Provider.ResolveActorRef(message.Sender);
            }

            var repr = new Persistent(
                GetPayload(message.Payload),
                message.SequenceNr,
                message.PersistenceId,
                message.Manifest,
                message.Deleted,
                sender,
                message.WriterGuid);

            return message.Timestamp > 0L ? repr.WithTimestamp(message.Timestamp) : repr;
        }

        private object GetPayload(PersistentPayload payload)
        {
            string manifest = "";
            if (payload.PayloadManifest != null) manifest = payload.PayloadManifest.ToStringUtf8();

            return system.Serialization.Deserialize(new ReadOnlySequence<byte>(payload.Payload.Memory), payload.SerializerId, manifest);
        }

        private AtomicWrite GetAtomicWrite(ReadOnlySequence<byte> bytes)
        {
            var message = Proto.Msg.AtomicWrite.Parser.ParseFrom(bytes);
            var payloads = new List<IPersistentRepresentation>();
            foreach (var payload in message.Payload)
            {
                payloads.Add(GetPersistentRepresentation(payload));
            }
            return new AtomicWrite(payloads.ToImmutableList());
        }

        private AtLeastOnceDeliverySnapshot GetAtLeastOnceDeliverySnapshot(ReadOnlySequence<byte> bytes)
        {
            var message = Proto.Msg.AtLeastOnceDeliverySnapshot.Parser.ParseFrom(bytes);

            var unconfirmedDeliveries = new List<UnconfirmedDelivery>();
            foreach (var unconfirmed in message.UnconfirmedDeliveries)
            {
                ActorPath.TryParse(unconfirmed.Destination, out var actorPath);
                unconfirmedDeliveries.Add(new UnconfirmedDelivery(unconfirmed.DeliveryId, actorPath, GetPayload(unconfirmed.Payload)));
            }

            return new AtLeastOnceDeliverySnapshot(message.CurrentDeliveryId, unconfirmedDeliveries.ToArray());
        }

        private PersistentFSM.StateChangeEvent GetStateChangeEvent(ReadOnlySequence<byte> bytes)
        {
            var message = PersistentStateChangeEvent.Parser.ParseFrom(bytes);
            TimeSpan? timeout = null;
            if (message.TimeoutMillis > 0)
            {
                timeout = TimeSpan.FromMilliseconds(message.TimeoutMillis);
            }
            return new PersistentFSM.StateChangeEvent(message.StateIdentifier, timeout);
        }

    }
}
