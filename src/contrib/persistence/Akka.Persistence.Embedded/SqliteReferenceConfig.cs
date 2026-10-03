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
              journal {
                embedded {
                  class = "Akka.Persistence.Embedded.Journal.SqliteWriteJournal, Akka.Persistence.Embedded"
                  plugin-dispatcher = "akka.persistence.dispatchers.default-plugin-dispatcher"

                  # Microsoft.Data.Sqlite connection string. Required.
                  # Busy waiting is controlled by its "Default Timeout" keyword (seconds, default 30).
                  # The plugin opens its long-lived connections with Pooling=False unless this string sets Pooling itself.
                  connection-string = ""

                  # Only "default" is supported.
                  table-mapping = default

                  # If true, journal_metadata is created and used for deletes and highest sequence numbers.
                  delete-compatibility-mode = false

                  # Csv | TagTable | Both
                  tag-write-mode = TagTable

                  # Separator for the Csv tags column. Used when tag-write-mode is Csv or Both.
                  tag-separator = ";"

                  # Create missing tables and indexes on start (CREATE ... IF NOT EXISTS, never ALTER).
                  auto-initialize = true

                  # Accepted for Akka.Persistence.Sql compatibility. No effect (it has none in Akka.Persistence.Sql either).
                  warn-on-auto-init-fail = true

                  # Serializer name used when a payload type has no serialization-bindings entry.
                  # null = use the System.Object binding.
                  serializer = null

                  # Max write requests (one per WriteMessagesAsync call) queued for the writer thread. Further writes fail.
                  buffer-size = 5000

                  # Max rows per write transaction. A single write request larger than this is never split.
                  batch-size = 100

                  # Rows per round trip during recovery.
                  replay-batch-size = 1000

                  # Threads (each with its own connection) that run recovery reads and highest-sequence-number reads.
                  read-threads = 2

                  default {
                    schema-name = null
                    journal {
                      use-writer-uuid-column = true
                      table-name = "journal"
                      columns {
                        ordering = ordering
                        deleted = deleted
                        persistence-id = persistence_id
                        sequence-number = sequence_number
                        created = created
                        tags = tags
                        message = message
                        identifier = identifier
                        manifest = manifest
                        writer-uuid = writer_uuid
                      }
                    }
                    metadata {
                      table-name = "journal_metadata"
                      columns {
                        persistence-id = persistence_id
                        sequence-number = sequence_number
                      }
                    }
                    tag {
                      table-name = "tags"
                      columns {
                        ordering-id = ordering_id
                        tag-value = tag
                        persistence-id = persistence_id
                        sequence-nr = sequence_nr
                      }
                    }
                  }
                }
              }

              snapshot-store {
                embedded {
                  class = "Akka.Persistence.Embedded.Snapshot.SqliteSnapshotStore, Akka.Persistence.Embedded"
                  plugin-dispatcher = "akka.persistence.dispatchers.default-plugin-dispatcher"
                  connection-string = ""
                  table-mapping = default
                  serializer = null
                  auto-initialize = true
                  warn-on-auto-init-fail = true
                  default {
                    schema-name = null
                    snapshot {
                      table-name = "snapshot"
                      columns {
                        persistence-id = persistence_id
                        sequence-number = sequence_number
                        created = created
                        snapshot = snapshot
                        manifest = manifest
                        serializerId = serializer_id
                      }
                    }
                  }
                }
              }

              query.journal.embedded {
                class = "Akka.Persistence.Embedded.Query.SqliteReadJournalProvider, Akka.Persistence.Embedded"

                # Journal plugin whose tables, connection string, tag settings and event adapters this read journal uses.
                write-plugin = "akka.persistence.journal.embedded"

                # Empty = use the write plugin's connection-string.
                connection-string = ""

                # auto = follow the write plugin's tag-write-mode (Both -> TagTable). Or Csv | TagTable.
                tag-read-mode = auto

                # Rows per query round trip (all queries).
                max-buffer-size = 500

                # Poll interval of live queries when the last batch was not full.
                refresh-interval = 1s

                # At most this many queries run or wait at once; a query that cannot start within
                # query-throttle-timeout fails with a TimeoutException.
                max-concurrent-queries = 100
                query-throttle-timeout = 3s

                # Threads (each with its own connection) that execute query SQL.
                query-threads = 4

                # How long a query waits for the write plugin to finish initializing (table creation).
                write-plugin-init-timeout = 10s

                journal-sequence-retrieval {
                  # off = bound each batch by MAX(ordering) read in the same transaction (default).
                  # on  = Akka.Persistence.Sql's gap-tracking actor; the keys below apply.
                  enabled = off
                  batch-size = 10000
                  max-tries = 10
                  query-delay = 1s
                  max-backoff-query-delay = 60s
                  ask-timeout = 1s
                }
              }
            }
            """;
    }
}
