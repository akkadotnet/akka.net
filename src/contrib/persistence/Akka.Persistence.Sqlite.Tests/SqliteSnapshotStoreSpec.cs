//-----------------------------------------------------------------------
// <copyright file="SqliteSnapshotStoreSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TCK.Snapshot;
using Akka.Util.Internal;
using Xunit.Abstractions;

namespace Akka.Persistence.Sqlite.Tests
{
    public class SqliteSnapshotStoreSpec : SnapshotStoreSpec
    {
        private static AtomicCounter counter = new AtomicCounter(0);
        public SqliteSnapshotStoreSpec(ITestOutputHelper output)
            : base(CreateSpecConfig("Filename=file:memdb-snapshot-" + counter.IncrementAndGet() + ".db;Mode=Memory;Cache=Shared"), "SqliteSnapshotStoreSpec", output)
        {
            SqlitePersistence.Get(Sys);

            Initialize();
        }

        private static Config CreateSpecConfig(string connectionString)
        {
            return ConfigurationFactory.ParseString(@"
                akka.persistence {
                    publish-plugin-commands = on
                    snapshot-store {
                        plugin = ""akka.persistence.snapshot-store.sqlite""
                        sqlite {
                            class = ""Akka.Persistence.Sqlite.Snapshot.SqliteSnapshotStore, Akka.Persistence.Sqlite""
                            plugin-dispatcher = ""akka.actor.default-dispatcher""
                            table-name = snapshot_store
                            auto-initialize = on
                            connection-string = """ + connectionString + @"""
                        }
                    }
                }");
        }

        protected override bool SupportsSerialization => true;
    }
}
