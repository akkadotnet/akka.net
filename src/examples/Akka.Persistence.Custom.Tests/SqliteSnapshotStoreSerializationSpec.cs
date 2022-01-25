// //-----------------------------------------------------------------------
// // <copyright file="SqliteSnapshotStoreSerializationSpec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.Persistence.TCK.Serialization;
using Xunit.Abstractions;

namespace Akka.Persistence.Custom.Tests
{
    public class SqliteSnapshotStoreSerializationSpec: SnapshotStoreSerializationSpec
    {
        public SqliteSnapshotStoreSerializationSpec(ITestOutputHelper output)
            : base(CreateSpecConfig("Filename=file:serialization-snapshot-" + Guid.NewGuid() + ".db"), "SqliteSnapshotStoreSerializationSpec", output)
        {
        }

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
    }
}