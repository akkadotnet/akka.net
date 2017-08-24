//-----------------------------------------------------------------------
// <copyright file="IMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-------------------

using System;
using Akka.Actor;
using Akka.Persistence.Serialization.Proto.Msg;
using Akka.Serialization;
using Google.Protobuf;

namespace Akka.Persistence.Serialization
{
    public class PersistenceSnapshotSerializer : Serializer
    {
        public PersistenceSnapshotSerializer(ExtendedActorSystem system) : base(system)
        {
            IncludeManifest = true;
        }

        public override bool IncludeManifest { get; }

        public override byte[] ToBinary(object obj)
        {
            if (obj is Snapshot) return GetPersistentPayload(obj as Snapshot).ToByteArray();

            throw new ArgumentException($"Can't serialize object of type [{obj.GetType()}] in [{GetType()}]");
        }

        private PersistentPayload GetPersistentPayload(Snapshot snapshot)
        {
            Serializer serializer = system.Serialization.FindSerializerFor(snapshot.Data);
            PersistentPayload payload = new PersistentPayload();

            if (serializer is SerializerWithStringManifest)
            {
                string manifest = ((SerializerWithStringManifest)serializer).Manifest(snapshot.Data);
                payload.PayloadManifest = ByteString.CopyFromUtf8(manifest);
            }
            else
            {
                if (serializer.IncludeManifest)
                {
                    var payloadType = snapshot.Data.GetType();
                    payload.PayloadManifest = ByteString.CopyFromUtf8(payloadType.AssemblyQualifiedName);
                }
            }

            payload.Payload = ByteString.CopyFrom(serializer.ToBinary(snapshot.Data));
            payload.SerializerId = serializer.Identifier;

            return payload;
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            if (type == typeof(Snapshot)) return GetSnapshot(bytes);

            throw new ArgumentException($"Unimplemented deserialization of message with type [{type}] in [{GetType()}]");
        }

        private Snapshot GetSnapshot(byte[] bytes)
        {
            PersistentPayload payload = PersistentPayload.Parser.ParseFrom(bytes);

            string manifest = "";
            if (payload.PayloadManifest != null) manifest = payload.PayloadManifest.ToStringUtf8();

            return new Snapshot(system.Serialization.Deserialize(payload.Payload.ToByteArray(), payload.SerializerId, manifest));
        }
    }
}