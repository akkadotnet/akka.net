//-----------------------------------------------------------------------
// <copyright file="PersistenceSnapshotSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
            if (obj is Snapshot snapshot) return GetPersistentPayload(snapshot).ToByteArray();

            throw new ArgumentException($"Can't serialize object of type [{obj.GetType()}] in [{GetType()}]");
        }

        private PersistentPayload GetPersistentPayload(Snapshot snapshot)
        {
            PersistentPayload Serialize()
            {
                var serializer = system.Serialization.FindSerializerFor(snapshot.Data);
                var payload = new PersistentPayload();

                var manifest = Akka.Serialization.Serialization.ManifestFor(serializer, snapshot.Data);
                if (!string.IsNullOrEmpty(manifest))
                {
                    payload.PayloadManifest = ByteString.CopyFromUtf8(manifest);
                }

                payload.Payload = ByteString.CopyFrom(serializer.ToBinary(snapshot.Data));
                payload.SerializerId = serializer.Identifier;

                return payload;
            }

            var oldInfo = Akka.Serialization.Serialization.CurrentTransportInformation;
            try
            {
                if (oldInfo == null)
                    Akka.Serialization.Serialization.CurrentTransportInformation =
                        system.Provider.SerializationInformation;
                return Serialize();
            }
            finally
            {
                Akka.Serialization.Serialization.CurrentTransportInformation = oldInfo;
            }
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
