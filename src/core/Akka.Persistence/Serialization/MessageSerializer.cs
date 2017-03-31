//-----------------------------------------------------------------------
// <copyright file="MessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Serialization;
using Akka.Util;
using Google.ProtocolBuffers;

namespace Akka.Persistence.Serialization
{
    /// <summary>
    /// TBD
    /// </summary>
    public interface IMessage { }

    /// <summary>
    /// TBD
    /// </summary>
    public class MessageSerializer : Serializer
    {
        private Information _transportInformation;

        /// <summary>
        /// Initializes a new instance of the <see cref="MessageSerializer"/> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer.</param>
        public MessageSerializer(ExtendedActorSystem system)
            : base(system)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal Information TransportInformation
        {
            get
            {
                return _transportInformation ?? (_transportInformation = GetTransportInformation());
            }
        }

        /// <summary>
        /// Returns whether this serializer needs a manifest in the fromBinary method
        /// </summary>
        public override bool IncludeManifest
        {
            get { return true; }
        }

        /// <summary>
        /// Serializes the given object into a byte array
        /// </summary>
        /// <param name="obj">The object to serialize</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the <see cref="MessageSerializer"/> cannot serialize the specified <paramref name="obj"/>.
        /// The specified <paramref name="obj" /> must be of one of the following types:
        /// <see cref="IPersistentRepresentation"/> | <see cref="AtomicWrite"/> | <see cref="AtLeastOnceDeliverySnapshot"/>.
        /// </exception>
        /// <returns>
        /// A byte array containing the serialized object
        /// </returns>
        public override byte[] ToBinary(object obj)
        {
            if (obj is IPersistentRepresentation) return PersistentToProto(obj as IPersistentRepresentation).Build().ToByteArray();
            if (obj is AtomicWrite) return AtomicWriteToProto(obj as AtomicWrite).Build().ToByteArray();
            if (obj is AtLeastOnceDeliverySnapshot) return SnapshotToProto(obj as AtLeastOnceDeliverySnapshot).Build().ToByteArray();
            // TODO StateChangeEvent

            throw new ArgumentException($"{typeof(MessageSerializer)} cannot serialize object of type {obj.GetType()}", nameof(obj));
        }

        /// <summary>
        /// Deserializes a byte array into an object of type <paramref name="type" />.
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <param name="type">The type of object contained in the array</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the <see cref="MessageSerializer"/> cannot deserialize the specified <paramref name="type"/>.
        /// The specified <paramref name="type" /> must be of one of the following types:
        /// <see cref="IPersistentRepresentation"/> | <see cref="AtomicWrite"/> | <see cref="AtLeastOnceDeliverySnapshot"/>.
        /// </exception>
        /// <returns>
        /// The object contained in the array
        /// </returns>
        public override object FromBinary(byte[] bytes, Type type)
        {
            if (type == null || type == typeof(Persistent) || type == typeof(IPersistentRepresentation)) return PersistentMessageFrom(bytes);
            if (type == typeof(AtomicWrite)) return AtomicWriteFrom(bytes);
            if (type == typeof(AtLeastOnceDeliverySnapshot)) return SnapshotFrom(bytes);
            // TODO StateChangeEvent
            // TODO PersistentStateChangeEvent

            throw new ArgumentException($"{typeof(MessageSerializer)} cannot deserialize object of type {type}", nameof(type));
        }

        private AtLeastOnceDeliverySnapshot SnapshotFrom(byte[] bytes)
        {
            var snap = global::AtLeastOnceDeliverySnapshot.ParseFrom(bytes);
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

            return new AtLeastOnceDeliverySnapshot(snap.CurrentDeliveryId, unconfirmedDeliveries);
        }

        private IPersistentRepresentation PersistentMessageFrom(byte[] bytes)
        {
            var persistentMessage = PersistentMessage.ParseFrom(bytes);

            return PersistentMessageFrom(persistentMessage);
        }

        private IPersistentRepresentation PersistentMessageFrom(PersistentMessage persistentMessage)
        {
            return new Persistent(
                payload: PayloadFromProto(persistentMessage.Payload),
                sequenceNr: persistentMessage.SequenceNr,
                persistenceId: persistentMessage.HasPersistenceId ? persistentMessage.PersistenceId : null,
                manifest: persistentMessage.HasManifest ? persistentMessage.Manifest : null,
                // isDeleted is not used in new records from 1.5
                sender: persistentMessage.HasSender ? system.Provider.ResolveActorRef(persistentMessage.Sender) : null,
                writerGuid: persistentMessage.HasWriterUuid ? persistentMessage.WriterUuid : null);
        }

        private object PayloadFromProto(PersistentPayload persistentPayload)
        {
            var manifest = persistentPayload.HasPayloadManifest
                ? persistentPayload.PayloadManifest.ToStringUtf8()
                : string.Empty;

            return system.Serialization.Deserialize(persistentPayload.Payload.ToByteArray(), persistentPayload.SerializerId, manifest);
        }

        private AtomicWrite AtomicWriteFrom(byte[] bytes)
        {
            var atomicWrite = global::AtomicWrite.ParseFrom(bytes);

            return new AtomicWrite(atomicWrite.PayloadList.Select(PersistentMessageFrom).ToImmutableList());
        }

        private global::AtLeastOnceDeliverySnapshot.Builder SnapshotToProto(AtLeastOnceDeliverySnapshot snap)
        {
            var builder = global::AtLeastOnceDeliverySnapshot.CreateBuilder();
            builder.SetCurrentDeliveryId(snap.CurrentDeliveryId);

            foreach (var unconfirmed in snap.UnconfirmedDeliveries)
            {
                var unconfirmedBuilder = global::AtLeastOnceDeliverySnapshot.Types.UnconfirmedDelivery.CreateBuilder()
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
            if (p.Manifest != null) builder.SetManifest(p.Manifest);

            builder
                .SetPayload(PersistentPayloadToProto(p.Payload))
                .SetSequenceNr(p.SequenceNr);
                // deleted is not used in new records

           if (p.WriterGuid != null) builder.SetWriterUuid(p.WriterGuid);

            return builder;
        }

        private PersistentPayload.Builder PersistentPayloadToProto(object payload)
        {
            return TransportInformation != null
                ? Akka.Serialization.Serialization.SerializeWithTransport(TransportInformation.System,
                    TransportInformation.Address, () => PayloadBuilder(payload))
                : PayloadBuilder(payload);
        }

        private PersistentPayload.Builder PayloadBuilder(object payload)
        {
            var serializer = system.Serialization.FindSerializerFor(payload);
            var builder = PersistentPayload.CreateBuilder();

            if (serializer is SerializerWithStringManifest)
            {
                var manifest = ((SerializerWithStringManifest) serializer).Manifest(payload);
                if (manifest != null)
                    builder.SetPayloadManifest(ByteString.CopyFromUtf8(manifest));
            }
            else if (serializer.IncludeManifest)
                builder.SetPayloadManifest(ByteString.CopyFromUtf8(payload.GetType().TypeQualifiedName()));

            var bytes = serializer.ToBinary(payload);

            builder
                .SetPayload(ByteString.CopyFrom(bytes))
                .SetSerializerId(serializer.Identifier);

            return builder;
        }

        private global::AtomicWrite.Builder AtomicWriteToProto(AtomicWrite aw)
        {
            var builder = global::AtomicWrite.CreateBuilder();

            foreach (var p in (IEnumerable<IPersistentRepresentation>)aw.Payload)
            {
                builder.AddPayload(PersistentToProto(p));
            }
            

            return builder;
        }

        private Information GetTransportInformation()
        {
            var address = system.Provider.DefaultAddress;
            return !string.IsNullOrEmpty(address.Host)
                ? new Information { Address = address, System = system }
                : new Information { System = system };
        }
    }
}
