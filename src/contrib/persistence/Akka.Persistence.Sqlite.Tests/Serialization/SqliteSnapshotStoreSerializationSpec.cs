//-----------------------------------------------------------------------
// <copyright file="SqliteSnapshotStoreSerializationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TCK.Serialization;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Sqlite.Tests.Serialization
{
    public class SqliteSnapshotStoreSerializationSpec : SnapshotStoreSerializationSpec
    {
        private static AtomicCounter Counter { get; } = new AtomicCounter(0);

        public SqliteSnapshotStoreSerializationSpec(ITestOutputHelper output)
            : base(CreateSpecConfig("Filename=file:serialization-snapshot-" + Counter.IncrementAndGet() + ".db;Mode=Memory;Cache=Shared"), "SqliteSnapshotStoreSerializationSpec", output)
        {
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
    }
}
