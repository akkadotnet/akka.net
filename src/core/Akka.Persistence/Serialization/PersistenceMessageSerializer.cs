//-----------------------------------------------------------------------
// <copyright file="IMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Persistence.Serialization.Proto.Msg;
using Akka.Serialization;
using Google.Protobuf;

namespace Akka.Persistence.Serialization
{
    public class PersistenceMessageSerializer : Serializer
    {
        public PersistenceMessageSerializer(ExtendedActorSystem system) : base(system)
        {
            IncludeManifest = true;
        }

        public override bool IncludeManifest { get; }

        public override byte[] ToBinary(object obj)
        {
            if (obj is IPersistentRepresentation) return GetPersistentMessage((IPersistentRepresentation)obj).ToByteArray();
            if (obj is AtomicWrite) return GetAtomicWrite((AtomicWrite)obj).ToByteArray();
            if (obj is AtLeastOnceDeliverySnapshot) return GetAtLeastOnceDeliverySnapshot((AtLeastOnceDeliverySnapshot)obj).ToByteArray();

            throw new ArgumentException($"Can't serialize object of type [{obj.GetType()}] in [{GetType()}]");
        }

        private PersistentMessage GetPersistentMessage(IPersistentRepresentation persistent)
        {
            PersistentMessage message = new PersistentMessage();

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
            Serializer serializer = system.Serialization.FindSerializerFor(obj);
            PersistentPayload payload = new PersistentPayload();

            if (serializer is SerializerWithStringManifest)
            {
                string manifest = ((SerializerWithStringManifest)serializer).Manifest(obj);
                payload.PayloadManifest = ByteString.CopyFromUtf8(manifest);
            }
            else
            {
                if (serializer.IncludeManifest)
                {
                    var payloadType = obj.GetType();
                    payload.PayloadManifest = ByteString.CopyFromUtf8(payloadType.AssemblyQualifiedName);
                }
            }

            payload.Payload = ByteString.CopyFrom(serializer.ToBinary(obj));
            payload.SerializerId = serializer.Identifier;

            return payload;
        }

        private Proto.Msg.AtomicWrite GetAtomicWrite(AtomicWrite write)
        {
            Proto.Msg.AtomicWrite message = new Proto.Msg.AtomicWrite();
            foreach (var pr in (IImmutableList<IPersistentRepresentation>)write.Payload)
            {
                message.Payload.Add(GetPersistentMessage(pr));
            }
            return message;
        }

        private Proto.Msg.AtLeastOnceDeliverySnapshot GetAtLeastOnceDeliverySnapshot(AtLeastOnceDeliverySnapshot snapshot)
        {
            Proto.Msg.AtLeastOnceDeliverySnapshot message = new Proto.Msg.AtLeastOnceDeliverySnapshot
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

        public override object FromBinary(byte[] bytes, Type type)
        {
            if (type == typeof(Persistent)) return GetPersistentRepresentation(PersistentMessage.Parser.ParseFrom(bytes));
            if (type == typeof(IPersistentRepresentation)) return GetPersistentRepresentation(PersistentMessage.Parser.ParseFrom(bytes));
            if (type == typeof(AtomicWrite)) return GetAtomicWrite(bytes);
            if (type == typeof(AtLeastOnceDeliverySnapshot)) return GetAtLeastOnceDeliverySnapshot(bytes);

            throw new ArgumentException($"Unimplemented deserialization of message with type [{type}] in [{GetType()}]");
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
            Proto.Msg.AtomicWrite message = Proto.Msg.AtomicWrite.Parser.ParseFrom(bytes);
            var payloads = new List<IPersistentRepresentation>();
            foreach (var payload in message.Payload)
            {
                payloads.Add(GetPersistentRepresentation(payload));
            }
            return new AtomicWrite(payloads.ToImmutableList());
        }

        private AtLeastOnceDeliverySnapshot GetAtLeastOnceDeliverySnapshot(byte[] bytes)
        {
            Proto.Msg.AtLeastOnceDeliverySnapshot message = Proto.Msg.AtLeastOnceDeliverySnapshot.Parser.ParseFrom(bytes);

            var unconfirmedDeliveries = new List<UnconfirmedDelivery>();
            foreach (var unconfirmed in message.UnconfirmedDeliveries)
            {
                ActorPath.TryParse(unconfirmed.Destination, out var actorPath);
                unconfirmedDeliveries.Add(new UnconfirmedDelivery(unconfirmed.DeliveryId, actorPath, GetPayload(unconfirmed.Payload)));
            }

            return new AtLeastOnceDeliverySnapshot(message.CurrentDeliveryId, unconfirmedDeliveries.ToArray());
        }
    }
}