using System;
using Akka.Persistence.Sql.Linq2Db.Config;
using Akka.Persistence.Sql.Linq2Db.Journal;
using Akka.Serialization;
using Akka.Util;

namespace Akka.Persistence.Sql.Linq2Db.Snapshot
{
    public class ByteArraySnapshotSerializer : ISnapshotSerializer<SnapshotRow>
    {
        private Akka.Serialization.Serialization _serialization;
        private SnapshotConfig _config;

        public ByteArraySnapshotSerializer(Akka.Serialization.Serialization serialization, SnapshotConfig config)
        {
            _serialization = serialization;
            _config = config;
        }


        public Try<SnapshotRow> Serialize(SnapshotMetadata metadata, object snapshot)
        {
            return Try<SnapshotRow>.From(()=>ToSnapshotEntry(metadata, snapshot));
        }

        public Try<SelectedSnapshot> Deserialize(SnapshotRow t)
        {
            return Try<SelectedSnapshot>.From(() => ReadSnapshot(t));
        }
        protected SelectedSnapshot ReadSnapshot(SnapshotRow reader)
        {
            var metadata = new SnapshotMetadata(reader.PersistenceId, reader.SequenceNumber, reader.Created);
            var snapshot = GetSnapshot(reader);

            return new SelectedSnapshot(metadata, snapshot);
        }

        protected object GetSnapshot(SnapshotRow reader)
        {
            var manifest = reader.Manifest;
            var binary = (byte[])reader.Payload;

            object obj;
            if (reader.SerializerId == null)
            {
                var type = Type.GetType(manifest, true);
                // TODO: hack. Replace when https://github.com/akkadotnet/akka.net/issues/3811
                var serializer = _serialization.FindSerializerForType(type, _config.DefaultSerializer);
                obj = Akka.Serialization.Serialization.WithTransport(_serialization.System, () => serializer.FromBinary(binary, type));
            }
            else
            {
                var serializerId = reader.SerializerId.Value;
                obj = _serialization.Deserialize(binary, serializerId, manifest);
            }

            return obj;
        }
        private SnapshotRow ToSnapshotEntry(SnapshotMetadata metadata, object snapshot)
        {
            var snapshotType = snapshot.GetType();
            var serializer = _serialization.FindSerializerForType(snapshotType, _config.DefaultSerializer);
            var binary  = Akka.Serialization.Serialization.WithTransport(_serialization.System,
                () => serializer.ToBinary(snapshot));
            string manifest = "";
            if (serializer is SerializerWithStringManifest)
            {
                manifest = ((SerializerWithStringManifest)serializer).Manifest(snapshot);
            }
            else
            {
                if (serializer.IncludeManifest)
                {
                    manifest = snapshotType.TypeQualifiedName();
                }
            }
            return new SnapshotRow()
            {PersistenceId= metadata.PersistenceId,
                SequenceNumber= metadata.SequenceNr,
                Created= metadata.Timestamp,
                Manifest= manifest,
                Payload = binary, SerializerId = serializer.Identifier};
        }
    }
}