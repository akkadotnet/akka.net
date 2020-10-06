//-----------------------------------------------------------------------
// <copyright file="BatchingSqlitePersistenceIdSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Persistence.Query.Sql;
using Akka.Persistence.TCK.Query;
using Akka.Util.Internal;
using Xunit.Abstractions;

namespace Akka.Persistence.Sqlite.Tests.Batching
{
    public class BatchingSqlitePersistenceIdSpec : PersistenceIdsSpec
    {
        public static string ConnectionString(string type) => $"Filename=file:memdb-persistenceids-{type}-{Guid.NewGuid()}.db;Mode=Memory;Cache=Shared";

        public static Config Config => ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.actor{{
                serializers{{
                    persistence-tck-test=""Akka.Persistence.TCK.Serialization.TestSerializer,Akka.Persistence.TCK""
                }}
                serialization-bindings {{
                    ""Akka.Persistence.TCK.Serialization.TestPayload,Akka.Persistence.TCK"" = persistence-tck-test
                }}
            }}
            akka.persistence {{
                publish-plugin-commands = on
                journal {{
                    plugin = ""akka.persistence.journal.sqlite""
                    sqlite = {{
                        class = ""Akka.Persistence.Sqlite.Journal.SqliteJournal, Akka.Persistence.Sqlite""
                        plugin-dispatcher = ""akka.actor.default-dispatcher""
                        table-name = event_journal
                        metadata-table-name = journal_metadata
                        auto-initialize = on
                        connection-string = ""{ConnectionString("journal")}""
                        refresh-interval = 200ms
                    }}
                }}
                snapshot-store {{
                    plugin = ""akka.persistence.snapshot-store.sqlite""
                    sqlite {{
                        class = ""Akka.Persistence.Sqlite.Snapshot.SqliteSnapshotStore, Akka.Persistence.Sqlite""
                        plugin-dispatcher = ""akka.actor.default-dispatcher""
                        table-name = snapshot_store
                        auto-initialize = on
                        connection-string = ""{ConnectionString("snapshot")}""
                    }}
                }}
            }}
            akka.test.single-expect-default = 10s")
            .WithFallback(SqlReadJournal.DefaultConfiguration());
            

        public BatchingSqlitePersistenceIdSpec(ITestOutputHelper output) : base(Config, nameof(BatchingSqlitePersistenceIdSpec), output)
        {
            ReadJournal = Sys.ReadJournalFor<SqlReadJournal>(SqlReadJournal.Identifier);
        }
    }
}
