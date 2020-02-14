﻿//-----------------------------------------------------------------------
// <copyright file="SqliteSnapshotStoreConnectionFailureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Hocon;
using Akka.Persistence.Sql.TestKit;
using Xunit.Abstractions;

namespace Akka.Persistence.Sqlite.Tests
{
    public class SqliteSnapshotStoreConnectionFailureSpec : SqlSnapshotConnectionFailureSpec
    {
        public SqliteSnapshotStoreConnectionFailureSpec(ITestOutputHelper output)
            : base(CreateSpecConfig(DefaultInvalidConnectionString), output)
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
