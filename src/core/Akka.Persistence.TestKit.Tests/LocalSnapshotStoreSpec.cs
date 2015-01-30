using System.Linq;
using Akka.Configuration;
using Akka.Persistence.TestKit.Snapshot;

namespace Akka.Persistence.TestKit.Tests
{
    public class LocalSnapshotStoreSpec : SnapshotStoreSpec
    {
        public LocalSnapshotStoreSpec() 
            : base(ConfigurationFactory.ParseString(
                @"akka.test.timefactor = 3
                  akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.local""
                  akka.persistence.snapshot-store.local.dir = ""target/snapshots"""), 
            "LocalSnapshotStoreSpec")
        {
            Sys.DeleteStorageLocations("akka.persistence.snapshot-store.local.dir");
            Sys.CreateStorageLocations("akka.persistence.snapshot-store.local.dir");
            Metadata = WriteSnapshots().ToList();
        }

    }
}