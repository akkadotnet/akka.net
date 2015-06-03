namespace Akka.Persistence.Cassandra.Journal
{
    /// <summary>
    /// CQL strings for use with the CassandraJournal.
    /// </summary>
    internal static class JournalStatements
    {
        public const string CreateKeyspace = @"
            CREATE KEYSPACE IF NOT EXISTS {0}
            WITH {1}";

        public const string CreateTable = @"
            CREATE TABLE IF NOT EXISTS {0} (
                persistence_id text,
                partition_number bigint,
                marker text,
                sequence_number bigint,
                message blob,
                PRIMARY KEY ((persistence_id, partition_number), marker, sequence_number)
            ){1}{2}";

        public const string WriteMessage = @"
            INSERT INTO {0} (persistence_id, partition_number, marker, sequence_number, message)
            VALUES (?, ?, 'A', ?, ?)";

        public const string WriteHeader = @"
            INSERT INTO {0} (persistence_id, partition_number, marker, sequence_number)
            VALUES (?, ?, 'H', ?)";

        public const string SelectMessages = @"
            SELECT message FROM {0} WHERE persistence_id = ? AND partition_number = ?
            AND marker = 'A' AND sequence_number >= ? AND sequence_number <= ?";

        public const string WriteDeleteMarker = @"
            INSERT INTO {0} (persistence_id, partition_number, marker, sequence_number)
            VALUES (?, ?, 'D', ?)";

        public const string DeleteMessagePermanent = @"
            DELETE FROM {0} WHERE persistence_id = ? AND partition_number = ? 
            AND marker = 'A' AND sequence_number = ?";

        public const string SelectDeletedToSequence = @"
            SELECT sequence_number FROM {0} WHERE persistence_id = ? AND partition_number = ? 
            AND marker = 'D' ORDER BY marker DESC, sequence_number DESC LIMIT 1";

        public const string SelectLastMessageSequence = @"
            SELECT sequence_number FROM {0} WHERE persistence_id = ? AND partition_number = ? 
            AND marker = 'A' AND sequence_number >= ? 
            ORDER BY marker DESC, sequence_number DESC LIMIT 1";

        public const string SelectHeaderSequence = @"
            SELECT sequence_number FROM {0} WHERE persistence_id = ? AND partition_number = ? 
            AND marker = 'H'";

        public const string SelectConfigurationValue = @"
            SELECT message FROM {0}
            WHERE persistence_id = 'akkanet-configuration-values' AND partition_number = 0 AND marker = ?";

        public const string WriteConfigurationValue = @"
            INSERT INTO {0} (persistence_id, partition_number, marker, sequence_number, message) 
            VALUES ('akkanet-configuration-values', 0, ?, 0, ?)";
    }
}
