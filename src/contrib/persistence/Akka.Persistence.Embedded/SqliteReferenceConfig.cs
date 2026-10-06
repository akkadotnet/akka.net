//-----------------------------------------------------------------------
// <copyright file="SqliteReferenceConfig.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
namespace Akka.Persistence.Embedded
{
    internal static class SqliteReferenceConfig
    {
        // Kept in code, not in an embedded resource, so trimmed and Native AOT apps can read it.
        internal const string ReferenceHocon = """
            akka.persistence {
              journal.embedded {
                class = "Akka.Persistence.Embedded.Journal.SqliteWriteJournal, Akka.Persistence.Embedded"
                plugin-dispatcher = "akka.persistence.dispatchers.default-plugin-dispatcher"

                # Microsoft.Data.Sqlite connection string. Required.
                # Busy waiting is controlled by its "Default Timeout" keyword (seconds, default 30).
                # The plugin opens its long-lived connections with Pooling=False unless this string sets Pooling itself.
                connection-string = ""

                # Create missing tables and indexes on start (CREATE ... IF NOT EXISTS, never ALTER).
                auto-initialize = true

                # Table names. The column names are fixed: the layout Akka.Persistence.Sql creates on SQLite.
                table-name = "journal"
                tag-table-name = "tags"

                # Max write requests (one per WriteMessagesAsync call) queued for the writer thread. Further writes fail.
                buffer-size = 5000

                # Max rows per write transaction. A single write request larger than this is never split.
                batch-size = 100

                # Rows per round trip during recovery.
                replay-batch-size = 1000

                # Threads (each with its own connection) that run recovery reads and highest-sequence-number reads.
                read-threads = 2
              }

              snapshot-store.embedded {
                class = "Akka.Persistence.Embedded.Snapshot.SqliteSnapshotStore, Akka.Persistence.Embedded"
                plugin-dispatcher = "akka.persistence.dispatchers.default-plugin-dispatcher"

                # Microsoft.Data.Sqlite connection string. Required.
                connection-string = ""

                # Create the missing table and indexes on start.
                auto-initialize = true

                table-name = "snapshot"
              }

              query.journal.embedded {
                class = "Akka.Persistence.Embedded.Query.SqliteReadJournalProvider, Akka.Persistence.Embedded"

                # Journal plugin whose connection string, table names and event adapters this read journal uses.
                write-plugin = "akka.persistence.journal.embedded"

                # Rows per query round trip (all queries).
                max-buffer-size = 500

                # Poll interval of live queries when the last batch was not full.
                refresh-interval = 1s

                # Threads (each with its own connection) that execute query SQL.
                query-threads = 4
              }
            }
            """;
    }
}
