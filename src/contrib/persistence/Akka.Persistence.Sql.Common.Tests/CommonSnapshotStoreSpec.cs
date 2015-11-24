//-----------------------------------------------------------------------
// <copyright file="CommonSnapshotStoreSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TestKit.Snapshot;
using Akka.Util.Internal;
using Xunit.Abstractions;

namespace Akka.Persistence.Sql.Common.Tests
{
    public class CommonSnapshotStoreSpec : SnapshotStoreSpec
    {
        const string ConnectionString = @"Data Source=(localdb)\\MSSQLLocalDB;Integrated Security=true;MultipleActiveResultSets=True";

        private static AtomicCounter counter = new AtomicCounter(0);
        public CommonSnapshotStoreSpec(ITestOutputHelper output)
            : base(CreateSpecConfig(ConnectionString, string.Format("snapshot_store_spec_{0}", counter.IncrementAndGet())), "CommonSnapshotStoreSpec", output)
        {
            var exten = CommonPersistence.Get(Sys);

            exten.DropSnapshotTable();

            Initialize();
        }

        private static Config CreateSpecConfig(string connectionString, string tableName)
        {
            return ConfigurationFactory.ParseString(@"
                akka.persistence {
                    publish-plugin-commands = on
                    snapshot-store {
                        plugin = ""akka.persistence.snapshot-store.common""
                        common {
                            class = ""Akka.Persistence.Sql.Common.Snapshot.CommonSnapshotStore, Akka.Persistence.Sql.Common""
                            plugin-dispatcher = ""akka.actor.default-dispatcher""
                            table-name = """ + tableName + @"""
                            auto-initialize = on
                            connection-string = """ + connectionString + @"""
                        }
                    }
                }");
        }
    }
}