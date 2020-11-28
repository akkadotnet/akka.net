using System;
using Akka.Configuration;

namespace Akka.Persistence.Sql.Linq2Db.Config
{
    public class JournalTableColumnNames
    {
        

        public JournalTableColumnNames(Configuration.Config config)
        {
            var compat = (config.GetString("table-compatibility-mode", "")??"").ToLower();
            string colString;
            switch (compat)
            {
                case "sqlserver":
                    colString = "sqlserver-compat-column-names";
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
                .GetConfig($"tables.journal.{colString}").SafeWithFallback(
                    ConfigurationFactory.ParseString(FallBack).GetConfig($"tables.journal.{colString}"));
            Ordering =       cfg.GetString("ordering","ordering");
            Deleted =        cfg.GetString("deleted","deleted");
            PersistenceId =  cfg.GetString("PersistenceId", "persistence_id");
            SequenceNumber = cfg.GetString("sequenceNumber", "sequence_number");
            Created =        cfg.GetString("created", "created");
            Tags =           cfg.GetString("tags", "tags");
            Message =        cfg.GetString("message", "message");
            Identitifer =    cfg.GetString("identifier", "identifier");
            Manifest =       cfg.GetString("manifest", "manifest");
        }
        public string Ordering { get; }
        public string Deleted { get; }
        public string PersistenceId { get; }
        public string SequenceNumber { get; }
        public string Created { get; }
        public string Tags { get; }
        public string Message { get; }
        public string Identitifer { get; }
        public string Manifest { get; }
        
        protected bool Equals(JournalTableColumnNames other)
        {
            return Ordering == other.Ordering && Deleted == other.Deleted &&
                   PersistenceId == other.PersistenceId &&
                   SequenceNumber == other.SequenceNumber &&
                   Created == other.Created && Tags == other.Tags &&
                   Message == other.Message &&
                   Identitifer == other.Identitifer &&
                   Manifest == other.Manifest;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((JournalTableColumnNames) obj);
        }

        public override int GetHashCode()
        {
            var hashCode = new HashCode();
            hashCode.Add(Ordering);
            hashCode.Add(Deleted);
            hashCode.Add(PersistenceId);
            hashCode.Add(SequenceNumber);
            hashCode.Add(Created);
            hashCode.Add(Tags);
            hashCode.Add(Message);
            hashCode.Add(Identitifer);
            hashCode.Add(Manifest);
            return hashCode.ToHashCode();
        }
        public static readonly string FallBack = @"tables.journal
  { 
    sqlserver-compat-column-names {
          ""ordering"" = ""ordering""
        ""deleted"" = ""isdeleted""
        ""PersistenceId"" = ""PersistenceId""
        ""sequenceNumber"" = ""sequenceNr""
        ""created"" = ""Timestamp""
        ""tags"" = ""tags""
        ""message"" = ""payload""
        ""identifier"" = ""serializerid""
        ""manifest"" = ""manifest""
    }
    sqlite-compat-column-names {
    ""ordering"" = ""ordering""
    ""deleted"" = ""is_deleted""
    ""PersistenceId"" = ""persistence_Id""
    ""sequenceNumber"" = ""sequence_nr""
    ""created"" = ""Timestamp""
    ""tags"" = ""tags""
    ""message"" = ""payload""
    ""identifier"" = ""serializer_id""
    ""manifest"" = ""manifest""
    }
postgres-compat-column-names {
          ""ordering"" = ""ordering""
        ""deleted"" = ""is_deleted""
        ""PersistenceId"" = ""persistence_id""
        ""sequenceNumber"" = ""sequence_nr""
        ""created"" = ""created_at""
        ""tags"" = ""tags""
        ""message"" = ""payload""
        ""identifier"" = ""serializer_id""
        ""manifest"" = ""manifest""
    }
 column-names
 { 
 }
}";
    }
}