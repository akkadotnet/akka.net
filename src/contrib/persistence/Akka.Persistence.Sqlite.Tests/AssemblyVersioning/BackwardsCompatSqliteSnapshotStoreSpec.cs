//-----------------------------------------------------------------------
// <copyright file="BackwardsCompatSqliteSnapshotStoreSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Persistence.TCK.Snapshot;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Sqlite.Tests.AssemblyVersioning
{
    public class BackwardsCompatSqliteSnapshotStoreSpec : Akka.TestKit.Xunit2.TestKit
    {
        public BackwardsCompatSqliteSnapshotStoreSpec(ITestOutputHelper output) : base(CreateSpecConfig(@"Filename=file:AssemblyVersioning/samples/memdb-snapshot-1-v123-altered.db;"), "BackwardsCompatSqliteSnapshotStoreSpec", output)
        {           
        }

        [Fact]
        public void Can_read_snapshot_from_v123_sqlitedb()
        {
            var snapshotStoreRef = Persistence.Instance.Apply(Sys).SnapshotStoreFor(null);
            var senderProbe = CreateTestProbe();
            snapshotStoreRef.Tell(new LoadSnapshot("p-22", SnapshotSelectionCriteria.Latest, long.MaxValue), senderProbe.Ref);
            senderProbe.ExpectMsg<LoadSnapshotResult>(result =>
                result.ToSequenceNr == long.MaxValue
                && result.Snapshot != null
                && result.Snapshot.Snapshot.ToString() == "s-5");
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
                            auto-initialize = on
                            connection-string = """ + connectionString + @"""
                        }
                    }
                }");
        }
    }
}
