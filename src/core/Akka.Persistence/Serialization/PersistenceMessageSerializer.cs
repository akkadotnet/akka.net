//-----------------------------------------------------------------------
// <copyright file="IMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Persistence.Serialization.Proto.Msg;
using Akka.Serialization;
using Google.Protobuf;

namespace Akka.Persistence.Serialization
{
    public class PersistenceMessageSerializer : Serializer
    {
        private readonly Akka.Serialization.Serialization _serialization;

        public PersistenceMessageSerializer(ExtendedActorSystem system) : base(system)
        {
            IncludeManifest = true;
            _serialization = system.Serialization;
        }

        public override bool IncludeManifest { get; }

        public override byte[] ToBinary(object obj)
        {
            if (obj is IPersistentRepresentation) return GetPersistentMessage(obj as IPersistentRepresentation).ToByteArray();

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
            Serializer serializer = _serialization.FindSerializerFor(obj);
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

        public override object FromBinary(byte[] bytes, Type type)
        {
            if (type == typeof(IPersistentRepresentation)) return GetPersistentRepresentation(bytes);

            throw new ArgumentException($"Unimplemented deserialization of message with type [{type}] in [{GetType()}]");
        }

        private IPersistentRepresentation GetPersistentRepresentation(byte[] bytes)
        {
            PersistentMessage message = PersistentMessage.Parser.ParseFrom(bytes);

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

            return _serialization.Deserialize(payload.Payload.ToByteArray(), payload.SerializerId, manifest);
        }
    }
}