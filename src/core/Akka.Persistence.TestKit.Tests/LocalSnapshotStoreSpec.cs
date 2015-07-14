//-----------------------------------------------------------------------
// <copyright file="LocalSnapshotStoreSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Configuration;
using Akka.Persistence.TestKit.Snapshot;
using Xunit;

namespace Akka.Persistence.TestKit.Tests
{
    public class LocalSnapshotStoreSpec : SnapshotStoreSpec
    {
        private readonly string _path;
        public LocalSnapshotStoreSpec() 
            : base(ConfigurationFactory.ParseString(
                @"akka.test.timefactor = 3
                  akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.local""
                  akka.persistence.snapshot-store.local.dir = ""target/snapshots-" + Guid.NewGuid() + @""""), 
            "LocalSnapshotStoreSpec")
        {
            _path = Sys.Settings.Config.GetString("akka.persistence.snapshot-store.local.dir");
            Sys.CreateStorageLocations(_path);

            Initialize();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Sys.DeleteStorageLocations(_path);
        }

        [Fact]
        public void LocalSnapshotStore_can_snapshot_actors_with_PersistenceId_containing_invalid_path_characters()
        {
            var pid = @"p\/:*?-1";
            SnapshotStore.Tell(new SaveSnapshot(new SnapshotMetadata(pid, 1), "sample data"), TestActor);
            ExpectMsg<SaveSnapshotSuccess>();

            SnapshotStore.Tell(new LoadSnapshot(pid, SnapshotSelectionCriteria.Latest, long.MaxValue), TestActor);
            ExpectMsg<LoadSnapshotResult>(res => 
                res.Snapshot.Snapshot.Equals("sample data") 
                && res.Snapshot.Metadata.PersistenceId == pid
                && res.Snapshot.Metadata.SequenceNr == 1);
        }
    }
}

