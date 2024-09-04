// -----------------------------------------------------------------------
//  <copyright file="SqliteSnapshotStoreSaveSnapshotSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TCK.Snapshot;
using Akka.Util.Internal;
using Xunit.Abstractions;

namespace Akka.Persistence.Sqlite.Tests;

public class SqliteSnapshotStoreSaveSnapshotSpec: SnapshotStoreSaveSnapshotSpec
{
    private static readonly AtomicCounter Counter = new(0);
    
    public SqliteSnapshotStoreSaveSnapshotSpec(ITestOutputHelper output)
        : base(CreateSpecConfig("Filename=file:memdb-snapshot-" + Counter.IncrementAndGet() + ".db;Mode=Memory;Cache=Shared"), "SqliteSnapshotStoreSpec", output)
    {
        SqlitePersistence.Get(Sys);
    }

    private static Config CreateSpecConfig(string connectionString)
    {
        return ConfigurationFactory.ParseString(
            $$"""
            akka.persistence {
                publish-plugin-commands = on
                snapshot-store {
                    plugin = "akka.persistence.snapshot-store.sqlite"
                    sqlite {
                        class = "Akka.Persistence.Sqlite.Snapshot.SqliteSnapshotStore, Akka.Persistence.Sqlite"
                        plugin-dispatcher = "akka.actor.default-dispatcher"
                        table-name = snapshot_store
                        auto-initialize = on
                        connection-string = "{{connectionString}}"
                    }
                }
            }
            """);
    }

}