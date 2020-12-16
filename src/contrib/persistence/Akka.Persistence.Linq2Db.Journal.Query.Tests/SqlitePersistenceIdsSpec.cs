//-----------------------------------------------------------------------
// <copyright file="SqlitePersistenceIdsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Linq2Db.Journal.Query.Tests;
using Akka.Persistence.Query;
using Akka.Persistence.Sql.Linq2Db.Journal;
using Akka.Persistence.Sql.Linq2Db.Query;
using Akka.Persistence.TCK.Query;
using Akka.Util.Internal;
using LinqToDB;
using Xunit.Abstractions;

namespace Akka.Persistence.Sqlite.Tests.Query
{
    public class SqlitePersistenceIdsSpec : PersistenceIdsSpec
    {

        public static Config Config
        {
            get
            {
                var journalString =
                    $"Filename=file:memdb-l2db-persistenceids-journal-{Guid.NewGuid()}.db;Mode=Memory;Cache=Shared";
                var snapshotString =
                    $"Filename=file:memdb-l2db-persistenceids-snapshot-{Guid.NewGuid()}.db;Mode=Memory;Cache=Shared";
                ConnectionContext.Remember(journalString);
                ConnectionContext.Remember(snapshotString);
                return ConfigurationFactory.ParseString($@"
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
                    plugin = ""akka.persistence.journal.linq2db""
                    linq2db = {{
                        provider-name = ""{ProviderName.SQLiteMS}""
                        plugin-dispatcher = ""akka.actor.default-dispatcher""
                        table-name = event_journal
                        metadata-table-name = journal_metadata
                        auto-initialize = on
                        connection-string = ""{journalString}""
                        refresh-interval = 200ms
                tables{{
                  journal{{
                       auto-init = true
                  }}
                }}
                    }}
                }}
                snapshot-store {{
                    plugin = ""akka.persistence.snapshot-store.sqlite""
                    sqlite {{
                        class = ""Akka.Persistence.Sqlite.Snapshot.SqliteSnapshotStore, Akka.Persistence.Sqlite""
                        plugin-dispatcher = ""akka.actor.default-dispatcher""
                        table-name = snapshot_store
                        auto-initialize = on
                        connection-string = ""{snapshotString}""
                    }}
                }}
            }}
            akka.persistence.query.journal.linq2db
            {{
                provider-name = ""{ProviderName.SQLiteMS}""
                connection-string = ""{journalString}""
                table-name = event_journal
                metadata-table-name = journal_metadata
                write-plugin = ""akka.persistence.journal.linq2db""
            }}
            akka.test.single-expect-default = 10s")
                    .WithFallback(Linq2DbReadJournal.DefaultConfiguration)
                    .WithFallback(Linq2DbWriteJournal.DefaultConfiguration);
            }
        }

        public override Task
            ReadJournal_should_deallocate_AllPersistenceIds_publisher_when_the_last_subscriber_left()
        {
            return Task.CompletedTask;
        }

        public SqlitePersistenceIdsSpec(ITestOutputHelper output) : base(Config,
            nameof(SqlitePersistenceIdsSpec), output)
        {
            ReadJournal =
                Sys.ReadJournalFor<Linq2DbReadJournal>(Linq2DbReadJournal
                    .Identifier);
        }
    }
}
