// //-----------------------------------------------------------------------
// // <copyright file="SqliteSnapshotStoreSpec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.Persistence.TCK.Snapshot;
using Xunit.Abstractions;

namespace Akka.Persistence.Custom.Tests
{
    public class SqliteSnapshotStoreSpec : SnapshotStoreSpec
    {
        public SqliteSnapshotStoreSpec(ITestOutputHelper output)
            : base(CreateSpecConfig("Filename=file:memdb-snapshot-" + Guid.NewGuid() + ".db"), "SqliteSnapshotStoreSpec", output)
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

        protected override bool SupportsSerialization => true;
    }}