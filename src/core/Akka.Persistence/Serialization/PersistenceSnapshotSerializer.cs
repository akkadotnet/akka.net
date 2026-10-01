//-----------------------------------------------------------------------
// <copyright file="PersistenceSnapshotSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Persistence.Serialization.Proto.Msg;
using Akka.Serialization;
using Akka.Util;
using Google.Protobuf;

namespace Akka.Persistence.Serialization
{
    public class PersistenceSnapshotSerializer : Serializer
    {
        public PersistenceSnapshotSerializer(ExtendedActorSystem system) : base(system)
        {
            IncludeManifest = true;
        }

        /// <inheritdoc />
        /// <remarks>
        /// A subclass keeps resolving its own id from HOCON via <see cref="Serializer.Identifier"/>.
        /// </remarks>
        public override int Identifier => GetType() == typeof(PersistenceSnapshotSerializer) ? 8 : base.Identifier;

        public override bool IncludeManifest { get; }

        // The only manifest FromBinary(byte[], Type) below handles.
        private static readonly Dictionary<string, Type> ManifestTypes = TypeExtensions.ManifestTable(typeof(Snapshot));

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

        /// <summary>Resolves the manifest from <see cref="ManifestTypes"/> so this works without dynamic type loading.</summary>
        public override object FromBinary(byte[] bytes, string manifest)
            => ManifestTypes.TryResolveManifestType(manifest, out var type) ? FromBinary(bytes, type!) : base.FromBinary(bytes, manifest);

        private Snapshot GetSnapshot(byte[] bytes)
        {
            PersistentPayload payload = PersistentPayload.Parser.ParseFrom(bytes);

            string manifest = "";
            if (payload.PayloadManifest != null) manifest = payload.PayloadManifest.ToStringUtf8();

            return new Snapshot(system.Serialization.Deserialize(payload.Payload.ToByteArray(), payload.SerializerId, manifest));
        }
    }
}
