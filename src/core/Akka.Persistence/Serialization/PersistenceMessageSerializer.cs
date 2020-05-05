//-----------------------------------------------------------------------
// <copyright file="PersistenceMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Persistence.Fsm;
using Akka.Persistence.Serialization.Proto.Msg;
using Akka.Serialization;
using Akka.Util;
using Google.Protobuf;

namespace Akka.Persistence.Serialization
{
    public sealed class PersistenceMessageSerializer : Serializer
    {
        public PersistenceMessageSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override bool IncludeManifest { get; } = true;

        public override byte[] ToBinary(object obj)
        {
            if (obj is IPersistentRepresentation repr) return GetPersistentMessage(repr).ToByteArray();
            if (obj is AtomicWrite aw) return GetAtomicWrite(aw).ToByteArray();
            if (obj is AtLeastOnceDeliverySnapshot snap) return GetAtLeastOnceDeliverySnapshot(snap).ToByteArray();
            if (obj is PersistentFSM.StateChangeEvent stateEvent) return GetStateChangeEvent(stateEvent).ToByteArray();
            if (obj.GetType().GetTypeInfo().IsGenericType 
                && obj.GetType().GetGenericTypeDefinition() == typeof(PersistentFSM.PersistentFSMSnapshot<>)) return GetPersistentFSMSnapshot(obj).ToByteArray();

            throw new ArgumentException($"Can't serialize object of type [{obj.GetType()}] in [{GetType()}]");
        }

        private PersistentMessage GetPersistentMessage(IPersistentRepresentation persistent)
        {
            var message = new PersistentMessage();

            if (persistent.PersistenceId != null) message.PersistenceId = persistent.PersistenceId;
            if (persistent.Manifest != null) message.Manifest = persistent.Manifest;
            if (persistent.WriterGuid != null) message.WriterGuid = persistent.WriterGuid;
            if (persistent.Sender != null) message.Sender = Akka.Serialization.Serialization.SerializedActorPath(persistent.Sender);

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

        private PersistentFSMSnapshot GetPersistentFSMSnapshot(object obj)
        {
            Type type = obj.GetType();

            var message = new PersistentFSMSnapshot
            {
                StateIdentifier = (string)type.GetProperty("StateIdentifier")?.GetValue(obj),
                Data = GetPersistentPayload(type.GetProperty("Data")?.GetValue(obj))
            };
            TimeSpan? timeout = (TimeSpan?)type.GetProperty("Timeout")?.GetValue(obj);
            if (timeout.HasValue)
            {
                message.TimeoutMillis = (long)timeout.Value.TotalMilliseconds;
            }
            return message;
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            if (type == typeof(Persistent)) return GetPersistentRepresentation(PersistentMessage.Parser.ParseFrom(bytes));
            if (type == typeof(IPersistentRepresentation)) return GetPersistentRepresentation(PersistentMessage.Parser.ParseFrom(bytes));
            if (type == typeof(AtomicWrite)) return GetAtomicWrite(bytes);
            if (type == typeof(AtLeastOnceDeliverySnapshot)) return GetAtLeastOnceDeliverySnapshot(bytes);
            if (type == typeof(PersistentFSM.StateChangeEvent)) return GetStateChangeEvent(bytes);
            if (type.GetTypeInfo().IsGenericType
                && type.GetGenericTypeDefinition() == typeof(PersistentFSM.PersistentFSMSnapshot<>)) return GetPersistentFSMSnapshot(type, bytes);

            throw new SerializationException($"Unimplemented deserialization of message with type [{type}] in [{GetType()}]");
        }

        private IPersistentRepresentation GetPersistentRepresentation(PersistentMessage message)
        {
            IActorRef sender = ActorRefs.NoSender;
            if (message.Sender != null)
            {
                sender = system.Provider.ResolveActorRef(message.Sender);
            }

            return new Persistent(
                GetPayload(message.Payload),
                message.SequenceNr,
                message.PersistenceId,
                message.Manifest,
                message.Deleted,
                sender,
                message.WriterGuid);
        }

        private object GetPayload(PersistentPayload payload)
        {
            string manifest = "";
            if (payload.PayloadManifest != null) manifest = payload.PayloadManifest.ToStringUtf8();

            return system.Serialization.Deserialize(payload.Payload.ToByteArray(), payload.SerializerId, manifest);
        }

        private AtomicWrite GetAtomicWrite(byte[] bytes)
        {
            var message = Proto.Msg.AtomicWrite.Parser.ParseFrom(bytes);
            var payloads = new List<IPersistentRepresentation>();
            foreach (var payload in message.Payload)
            {
                payloads.Add(GetPersistentRepresentation(payload));
            }
            return new AtomicWrite(payloads.ToImmutableList());
        }

        private AtLeastOnceDeliverySnapshot GetAtLeastOnceDeliverySnapshot(byte[] bytes)
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

        private PersistentFSM.StateChangeEvent GetStateChangeEvent(byte[] bytes)
        {
            var message = PersistentStateChangeEvent.Parser.ParseFrom(bytes);
            TimeSpan? timeout = null;
            if (message.TimeoutMillis > 0)
            {
                timeout = TimeSpan.FromMilliseconds(message.TimeoutMillis);
            }
            return new PersistentFSM.StateChangeEvent(message.StateIdentifier, timeout);
        }

        private object GetPersistentFSMSnapshot(Type type, byte[] bytes)
        {
            var message = PersistentFSMSnapshot.Parser.ParseFrom(bytes);

            TimeSpan? timeout = null;
            if (message.TimeoutMillis > 0)
            {
                timeout = TimeSpan.FromMilliseconds(message.TimeoutMillis);
            }

            // use reflection to create the generic type of PersistentFSM.PersistentFSMSnapshot
            Type[] types = { typeof(string), type.GenericTypeArguments[0], typeof(TimeSpan?) };
            object[] arguments = { message.StateIdentifier, GetPayload(message.Data), timeout };

            return type.GetConstructor(types).Invoke(arguments);
        }
    }
}
