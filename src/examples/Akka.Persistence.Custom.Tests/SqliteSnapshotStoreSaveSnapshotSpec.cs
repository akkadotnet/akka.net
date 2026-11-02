// -----------------------------------------------------------------------
//  <copyright file="SqliteSnapshotStoreSaveSnapshotSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.Persistence.TCK.Snapshot;
using Xunit.Abstractions;

namespace Akka.Persistence.Custom.Tests;

public class SqliteSnapshotStoreSaveSnapshotSpec: SnapshotStoreSaveSnapshotSpec
{
    private static Config CreateSpecConfig(string connectionString)
    {
        return ConfigurationFactory.ParseString(@"
                akka.persistence {
                    publish-plugin-commands = on
                    snapshot-store {
                        plugin = ""akka.persistence.snapshot-store.custom-sqlite""
                        custom-sqlite {
                            class = ""Akka.Persistence.Custom.Snapshot.SqliteSnapshotStore, Akka.Persistence.Custom""
                            plugin-dispatcher = ""akka.actor.default-dispatcher""
                            auto-initialize = on
                            connection-string = """ + connectionString + @"""
                        }
                    }
                }");
    }
    
    public SqliteSnapshotStoreSaveSnapshotSpec(ITestOutputHelper output)
        : base(CreateSpecConfig("Filename=file:memdb-snapshot-" + Guid.NewGuid() + ".db"), nameof(SqliteSnapshotStoreSaveSnapshotSpec), output)
    {
        SqlitePersistence.Get(Sys);
    }
}