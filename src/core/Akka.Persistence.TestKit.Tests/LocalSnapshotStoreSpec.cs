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
    }
}

