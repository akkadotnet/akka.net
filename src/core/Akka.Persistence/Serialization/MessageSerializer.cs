//-----------------------------------------------------------------------
// <copyright file="MessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Serialization;
using Google.ProtocolBuffers;

namespace Akka.Persistence.Serialization
{
    public interface IMessage { }

    public class MessageSerializer : Serializer
    {
        private Information _transportInformation;

        public MessageSerializer(ExtendedActorSystem system)
            : base(system)
        {
        }

        public Information TransportInformation
        {
            get
            {
                return _transportInformation ?? (_transportInformation = GetTransportInformation());
            }
        }

        public override int Identifier
        {
            get { return 7; }
        }

        public override bool IncludeManifest
        {
            get { return true; }
        }

        public override byte[] ToBinary(object obj)
        {
            if (obj is IPersistentRepresentation) return PersistentToProto(obj as IPersistentRepresentation).Build().ToByteArray();
            if (obj is GuaranteedDeliverySnapshot) return SnapshotToProto(obj as GuaranteedDeliverySnapshot).Build().ToByteArray();

            throw new ArgumentException(typeof(MessageSerializer) + " cannot serialize object of type " + obj.GetType());
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            if (type == null || type == typeof(Persistent) || type == typeof(IPersistentRepresentation)) return PersistentMessageFrom(bytes);
            if (type == typeof(GuaranteedDeliverySnapshot)) return SnapshotFrom(bytes);

            throw new ArgumentException(typeof(MessageSerializer) + " cannot deserialize object of type " + type);
        }

        private GuaranteedDeliverySnapshot SnapshotFrom(byte[] bytes)
        {
            var snap = AtLeastOnceDeliverySnapshot.ParseFrom(bytes);
            var unconfirmedDeliveries = new UnconfirmedDelivery[snap.UnconfirmedDeliveriesCount];

            for (int i = 0; i < snap.UnconfirmedDeliveriesCount; i++)
            {
                var unconfirmed = snap.UnconfirmedDeliveriesList[i];
                var unconfirmedDelivery = new UnconfirmedDelivery(
                    deliveryId: unconfirmed.DeliveryId,
                    destination: ActorPath.Parse(unconfirmed.Destination),
                    message: PayloadFromProto(unconfirmed.Payload));
                unconfirmedDeliveries[i] = unconfirmedDelivery;
            }

            return new GuaranteedDeliverySnapshot(snap.CurrentDeliveryId, unconfirmedDeliveries);
        }

        private IPersistentRepresentation PersistentMessageFrom(byte[] bytes)
        {
            var persistentMessage = PersistentMessage.ParseFrom(bytes);

            return new Persistent(
                payload: PayloadFromProto(persistentMessage.Payload),
                sequenceNr: persistentMessage.SequenceNr,
                persistenceId: persistentMessage.HasPersistenceId ? persistentMessage.PersistenceId : null,
                isDeleted: persistentMessage.Deleted,
                sender: persistentMessage.HasSender ? system.Provider.ResolveActorRef(persistentMessage.Sender) : null);
        }

        private object PayloadFromProto(PersistentPayload persistentPayload)
        {
            var payloadType = persistentPayload.HasPayloadManifest
                ? Type.GetType(persistentPayload.PayloadManifest.ToStringUtf8())
                : null;

            return system.Serialization.Deserialize(persistentPayload.Payload.ToByteArray(), persistentPayload.SerializerId, payloadType);
        }

        private AtLeastOnceDeliverySnapshot.Builder SnapshotToProto(GuaranteedDeliverySnapshot snap)
        {
            var builder = AtLeastOnceDeliverySnapshot.CreateBuilder();
            builder.SetCurrentDeliveryId(snap.DeliveryId);

            foreach (var unconfirmed in snap.UnconfirmedDeliveries)
            {
                var unconfirmedBuilder = AtLeastOnceDeliverySnapshot.Types.UnconfirmedDelivery.CreateBuilder()
                    .SetDeliveryId(unconfirmed.DeliveryId)
                    .SetDestination(unconfirmed.Destination.ToString())
                    .SetPayload(PersistentPayloadToProto(unconfirmed.Message));

                builder.AddUnconfirmedDeliveries(unconfirmedBuilder);
            }

            return builder;
        }

        private PersistentMessage.Builder PersistentToProto(IPersistentRepresentation p)
        {
            var builder = PersistentMessage.CreateBuilder();

            if (p.PersistenceId != null) builder.SetPersistenceId(p.PersistenceId);
            if (p.Sender != null) builder.SetSender(Akka.Serialization.Serialization.SerializedActorPath(p.Sender));

            builder
                .SetPayload(PersistentPayloadToProto(p.Payload))
                .SetSequenceNr(p.SequenceNr)
                .SetDeleted(p.IsDeleted);

            return builder;
        }

        private PersistentPayload.Builder PersistentPayloadToProto(object payload)
        {
            if (TransportInformation != null)
            {
                Akka.Serialization.Serialization.CurrentTransportInformation = TransportInformation;
            }

            var serializer = system.Serialization.FindSerializerFor(payload);
            var builder = PersistentPayload.CreateBuilder();

            if (serializer.IncludeManifest) builder.SetPayloadManifest(ByteString.CopyFromUtf8(payload.GetType().FullName));

            builder
                .SetPayload(ByteString.CopyFrom(serializer.ToBinary(payload)))
                .SetSerializerId(serializer.Identifier);

            return builder;
        }

        private Information GetTransportInformation()
        {
            var address = system.Provider.DefaultAddress;
            return !string.IsNullOrEmpty(address.Host)
                ? new Information { Address = address, System = system }
                : null;
        }
    }
}
