using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Akka.Actor;
using Akka.Configuration;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Sqlite.Tests
{
    public class Bugfix4360Spec : Akka.TestKit.Xunit2.TestKit
    {
        public static Config TestConf = @"
akka.persistence {
journal {
plugin = ""akka.persistence.journal.sqlite""
sqlite {
class = ""Akka.Persistence.Sqlite.Journal.SqliteJournal, Akka.Persistence.Sqlite""
plugin-dispatcher = ""akka.actor.default-dispatcher""
connection-string = ""DataSource=AkkaJournalfxR16.db""
connection-timeout = 25s
table-name = event_journal
auto-initialize = on
}
}
snapshot-store {
plugin = ""akka.persistence.snapshot-store.sqlite""
sqlite {
class = ""Akka.Persistence.Sqlite.Snapshot.SqliteSnapshotStore, Akka.Persistence.Sqlite""
plugin-dispatcher = ""akka.actor.default-dispatcher""
connection-string = ""DataSource=AkkaSnapShotfxR16.db""
connection-timeout = 25s
table-name = snapshot_store
auto-initialize = on
}
}
#end persistence
}}";

        private class RecoverActor : UntypedPersistentActor
        {
            private HashSet<string> _values = new HashSet<string>();

            public class DoSnapshot
            {
                public static readonly DoSnapshot Instance = new DoSnapshot();
                private DoSnapshot() { }
            }

            public RecoverActor(IActorRef reply)
            {
                PersistenceId = Self.Path.Name;
                Reply = reply;
            }

            public IActorRef Reply { get; }
            public override string PersistenceId { get; }
            protected override void OnCommand(object message)
            {
                switch (message)
                {
                    case DoSnapshot _:
                        SaveSnapshot(_values.ToArray());
                        break;
                    case SaveSnapshotSuccess succ:
                        Reply.Tell(succ);
                        break;
                    case string str:
                        Persist(str, s =>
                        {
                            _values.Add(s);
                            Reply.Tell(s);
                        });
                        break;
                    default:
                        Unhandled(message);
                        break;
                }
            }

            protected override void OnRecover(object message)
            {
                switch (message)
                {
                    case SnapshotOffer offer:
                        if (offer.Snapshot is IEnumerable<string> strs)
                            _values = new HashSet<string>(strs);
                        break;
                    case string str:
                        _values.Add(str);
                        break;
                    case RecoveryCompleted _:
                        Reply.Tell(_values.ToList());
                        break;
                    default:
                        Unhandled(message);
                        break;
                }
            }

            protected override void OnRecoveryFailure(Exception reason, object message = null)
            {
                Reply.Tell(reason);
                base.OnRecoveryFailure(reason, message);
            }
        }

        public Bugfix4360Spec(ITestOutputHelper output) : base(TestConf, output: output) { }

        [Fact]
        public void Should_recover_persistentactor_sqlite()
        {
            var recoveryActor = Sys.ActorOf(Props.Create(() => new RecoverActor(TestActor)), ThreadLocalRandom.Current.Next(0, 100000).ToString());

            var r1 = ExpectMsg<IEnumerable<string>>();
            r1.Count().Should().Be(0); // empty recovery

            recoveryActor.Tell("foo");
            recoveryActor.Tell("bar");
            recoveryActor.Tell(RecoverActor.DoSnapshot.Instance);
            ExpectMsgAllOf("foo", "bar");
            ExpectMsg<SaveSnapshotSuccess>();

            Watch(recoveryActor);
            recoveryActor.Tell(PoisonPill.Instance);
            ExpectTerminated(recoveryActor);

            // recreate the actor and recover
            var recoveryActor2 = Sys.ActorOf(Props.Create(() => new RecoverActor(TestActor)), recoveryActor.Path.Name);

            var r2 = ExpectMsg<IEnumerable<string>>();
            r2.Should().Contain(new[] { "foo", "bar" });
        }

        [Fact]
        public void Should_override_default_Sqlite_fallback_values()
        {
            SqlitePersistence.Get(Sys);

            var journalConfig = Sys.Settings.Config.GetConfig("akka.persistence.journal.sqlite");
            Assert.Equal(TimeSpan.FromSeconds(25), journalConfig.GetTimeSpan("connection-timeout"));
            Assert.Equal("event_journal", journalConfig.GetString("table-name"));
            Assert.Equal("journal_metadata", journalConfig.GetString("metadata-table-name"));

            var snapshotConfig = Sys.Settings.Config.GetConfig("akka.persistence.snapshot-store.sqlite");

            Assert.False(snapshotConfig.IsNullOrEmpty());
            Assert.Equal("DataSource=AkkaSnapShotfxR16.db", snapshotConfig.GetString("connection-string"));
            Assert.Equal(TimeSpan.FromSeconds(25), snapshotConfig.GetTimeSpan("connection-timeout"));
            Assert.Equal("snapshot_store", snapshotConfig.GetString("table-name"));
        }
    }
}
