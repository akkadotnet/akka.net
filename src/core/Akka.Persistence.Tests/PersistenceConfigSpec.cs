//-----------------------------------------------------------------------
// <copyright file="PersistenceConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.Tests
{
    public class PersistenceConfigSpec : AkkaSpec
    {
        [Fact]
        public void Persistence_should_use_inmem_journal_by_default()
        {
            var persistence = Persistence.Instance.Apply(Sys);
            var journal = persistence.JournalFor(string.Empty); // get the default journal
            journal.Path.Name.ShouldBe("akka.persistence.journal.inmem");
        }

        [Fact]
        public void Persistence_should_use_local_snapshot_store_by_default()
        {
            var persistence = Persistence.Instance.Apply(Sys);
            var journal = persistence.SnapshotStoreFor(string.Empty); // get the default snapshot store
            journal.Path.Name.ShouldBe("akka.persistence.snapshot-store.local");
        }
    }
}