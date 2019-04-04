using Akka.Actor;
using Akka.Configuration;
using Akka.Util.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Akka.TestKit.Xunit2;
using Akka.TestKit;

namespace Akka.Persistence.Sqlite.Tests.Batching
{
    public class BatchingSqliteJournalDatabseFailureSpec : AkkaSpec
    {
        public static readonly AtomicCounter Counter = new AtomicCounter(0);
        public static Config Config(int id) => ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.sqlite""
            akka.persistence.journal.sqlite {{
                class = ""Akka.Persistence.Sqlite.Journal.BatchingSqliteJournal, Akka.Persistence.Sqlite""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                table-name = event_journal
                metadata-table-name = journal_metadata
                auto-initialize = on
                connection-string = ""Datasource=journal{id}.db""
                refresh-interval = 1s
            }}");

        public BatchingSqliteJournalDatabseFailureSpec(ITestOutputHelper output) : base(Config(Counter.GetAndIncrement()), output: output)
        {

        }

        [Fact(DisplayName = "BatchingSqliteJournal should be able to handle dabase failure")]
        public void BatchingSqliteJournal_Should_Handle_Database_Failure()
        {
            var myActor = Sys.ActorOf(Props.Create(() => new Myactor("A1")));
            myActor.Tell(new MyInitMessage(), TestActor);
            ExpectMsg<MyInitMessage>();
        }

        public class Myactor : UntypedPersistentActor
        {
            public Myactor(string persistenceId)
            {
                PersistenceId = persistenceId;
            }
            public sealed override string PersistenceId { get; }

            protected override void OnCommand(object message)
            {
                switch (message)
                {
                    case MyInitMessage i:
                        Persist(new MyInitMessage(), s =>
                        {
                            Sender.Tell(new MyInitMessage());

                        });
                        break;
                    case MySendMessage m:
                        Persist(new MySendMessage(), s =>
                        {
                            Sender.Tell(new MySendMessage());
                        });
                        break;
                }
            }

            protected override void OnRecover(object message)
            {
                switch (message)
                {
                    case MyInitMessage s:
                        break;
                }
            }
        }

        public class MyInitMessage
        {
            public string Init = "Init";
        }
        public class MySendMessage
        {
            public string Send = "Send";
        }
    }
}

