//-----------------------------------------------------------------------
// <copyright file="PersistenceConfigAutoStartSpec.cs" company="Akka.NET Project">
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
    public class PersistenceConfigAutoStartSpec : AkkaSpec
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

        private static readonly Config AutoStartConfig = ConfigurationFactory.ParseString(@"
            akka.loglevel = INFO
            akka.persistence.journal {
                test {
                    class = ""Akka.Persistence.Tests.PersistenceConfigAutoStartSpec+TestJournal, Akka.Persistence.Tests""
                    plugin-dispatcher = ""akka.actor.default-dispatcher""
                    test-value = ""A""
                }
                auto-start-journals = [akka.persistence.journal.test]
            }
            akka.persistence.snapshot-store {
                test {
                    class = ""Akka.Persistence.Tests.PersistenceConfigAutoStartSpec+TestSnapshotStore, Akka.Persistence.Tests""
                    plugin-dispatcher = ""akka.actor.default-dispatcher""
                    test-value = ""C""
                }
                auto-start-snapshot-stores = [akka.persistence.snapshot-store.test]
            }
        ");

        public PersistenceConfigAutoStartSpec(ITestOutputHelper output = null) : base(AutoStartConfig, output)
        {
        }

        [Fact]
        public void Persistence_should_auto_start_journal_and_snapshotstore_when_specified()
        {
            EventFilter.Info(message: "Auto-starting journal plugin `akka.persistence.journal.test`")
                .And.Info(message: "Auto-starting snapshot store `akka.persistence.snapshot-store.test`").Expect(2, () =>
            {
                var persistence = Persistence.Instance.Apply(Sys);
            });
        }
    }
}
