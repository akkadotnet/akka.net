//-----------------------------------------------------------------------
// <copyright file="MemorySnapshotStoreSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TCK.Snapshot;
using Xunit.Abstractions;

namespace Akka.Persistence.TCK.Tests
{
    public class MemorySnapshotStoreSpec : SnapshotStoreSpec
    {
        public MemorySnapshotStoreSpec(ITestOutputHelper output) 
            : base(ConfigurationFactory.ParseString(
                @"akka.test.timefactor = 3
                  akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.inmem"""), 
                  "MemorySnapshotStoreSpec",
                  output)
        {
            Initialize();
        }

    }
}
