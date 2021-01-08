//-----------------------------------------------------------------------
// <copyright file="PersistenceConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.Persistence.Snapshot;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Tests
{
    public class PersistenceConfigSpec : AkkaSpec
    {
        #region internal classes

        private sealed class TestRequest
        {
            public static readonly TestRequest Instance = new TestRequest();

            private TestRequest()
            {
            }
        }

        public class TestJournal : MemoryJournal
        {
            private readonly string _testValue;

            public TestJournal(Config config)
            {
                _testValue = config.GetString("test-value", null);
            }

            protected override bool AroundReceive(Receive receive, object message)
            {
                if (message is TestRequest)
                {
                    Sender.Tell(_testValue);
                    return true;
                }
                else return base.AroundReceive(receive, message);
            }
        }

        public class TestSnapshotStore : LocalSnapshotStore
        {
            private readonly string _testValue;

            public TestSnapshotStore(Config config)
            {
                _testValue = config.GetString("test-value", null);
            }

            protected override bool AroundReceive(Receive receive, object message)
            {
                if (message is TestRequest)
                {
                    Sender.Tell(_testValue);
                    return true;
                }
                else return base.AroundReceive(receive, message);
            }
        }

        #endregion

        private static readonly string SpecConfig = @"
            akka.persistence.journal {
                test1 {
                    class = ""Akka.Persistence.Tests.PersistenceConfigSpec+TestJournal, Akka.Persistence.Tests""
                    plugin-dispatcher = ""akka.actor.default-dispatcher""
                    test-value = ""A""
                }
                test2 {
                    class = ""Akka.Persistence.Tests.PersistenceConfigSpec+TestJournal, Akka.Persistence.Tests""
                    plugin-dispatcher = ""akka.actor.default-dispatcher""
                    test-value = ""B""
                }
            }
            akka.persistence.snapshot-store {
                test1 {
                    class = ""Akka.Persistence.Tests.PersistenceConfigSpec+TestSnapshotStore, Akka.Persistence.Tests""
                    plugin-dispatcher = ""akka.actor.default-dispatcher""
                    test-value = ""C""
                }
                test2 {
                    class = ""Akka.Persistence.Tests.PersistenceConfigSpec+TestSnapshotStore, Akka.Persistence.Tests""
                    plugin-dispatcher = ""akka.actor.default-dispatcher""
                    test-value = ""D""
                }
            }";

        public PersistenceConfigSpec(ITestOutputHelper output) : base(SpecConfig, output)
        {
        }

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

        [Fact]
        public void Persistence_should_be_able_to_register_the_same_journal_under_different_paths()
        {
            var persistence = Persistence.Instance.Apply(Sys);
            var journal1 = persistence.JournalFor("akka.persistence.journal.test1");
            var journal2 = persistence.JournalFor("akka.persistence.journal.test2");

            journal1.Tell(TestRequest.Instance, TestActor);
            ExpectMsg("A");

            journal2.Tell(TestRequest.Instance, TestActor);
            ExpectMsg("B");
        }

        [Fact]
        public void Persistence_should_be_able_to_register_the_same_snapshot_store_under_different_paths()
        {
            var persistence = Persistence.Instance.Apply(Sys);
            var snapshotStore1 = persistence.SnapshotStoreFor("akka.persistence.snapshot-store.test1");
            var snapshotStore2 = persistence.SnapshotStoreFor("akka.persistence.snapshot-store.test2");

            snapshotStore1.Tell(TestRequest.Instance, TestActor);
            ExpectMsg("C");

            snapshotStore2.Tell(TestRequest.Instance, TestActor);
            ExpectMsg("D");
        }
    }
}
