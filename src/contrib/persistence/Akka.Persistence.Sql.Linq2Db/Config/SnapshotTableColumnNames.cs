using System;
using Akka.Configuration;

namespace Akka.Persistence.Sql.Linq2Db.Config
{
    public class SnapshotTableColumnNames
    {
        public SnapshotTableColumnNames(Configuration.Config config)
        {
            var compat = (config.GetString("table-compatibility-mode", "")??"").ToLower();
            string colString;
            switch (compat)
            {
                case "sqlserver":
                    colString = "sql-server-compat-column-names";
                    break;
                case "sqlite":
                    colString = "sqlite-compat-column-names";
                    break;
                case "postgres":
                    colString = "postgres-compat-column-names";
                    break;
                default:
                    colString = "column-names";
                    break;
            }
            var cfg = config
                .GetConfig($"tables.snapshot.{colString}").SafeWithFallback(
                    ConfigurationFactory.ParseString(FallBack).GetConfig($"tables.snapshot.{colString}"));

            PersistenceId = cfg.GetString("persistenceId", "persistence_id");
            SequenceNumber = cfg.GetString("sequenceNumber", "sequence_number");
            Created = cfg.GetString("created", "created");
            Snapshot = cfg.GetString("snapshot", "snapshot");
            Manifest = cfg.GetString("manifest", "manifest");
            SerializerId = cfg.GetString("serializerId", "serializer_id");
        }
        public string PersistenceId { get; }
        public string SequenceNumber { get; }
        public string Created { get; }
        public string Snapshot { get; }
        public string Manifest { get; }
        public string SerializerId { get; }
        public override int GetHashCode()
        {
            return HashCode.Combine(PersistenceId, SequenceNumber, Created,
                Snapshot, Manifest, SerializerId);
        }

        private static readonly string FallBack =
            @"sql-server-compat-column-names {
                  persistenceId = ""PersistenceId""
                  sequenceNumber = ""sequencenr""
                  created = ""timestamp""
                  snapshot = ""snapshot""
                  manifest = ""manifest""
                  serializerId = ""serializerid""
                }
sqlite-compat-column-names {
                  persistenceId = ""persistence_id""
                  sequenceNumber = ""sequence_nr""
                  snapshot = ""payload""
                  manifest = ""manifest""
                  created = ""created_at""
                  serializerId = ""serializer_id""
                }
postgres-compat-column-names {
                  persistenceId: ""persistence_id"",
                  sequenceNumber: ""sequence_nr"",
                  snapshot: ""payload"",
                  manifest: ""manifest"",
                  created: ""created_at"",
                  serializerId: ""serializer_id"",
                }";
    }
}