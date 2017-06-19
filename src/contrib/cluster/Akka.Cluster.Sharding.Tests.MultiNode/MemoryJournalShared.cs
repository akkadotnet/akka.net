using Akka.Actor;
using Akka.Persistence.Journal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Cluster.Sharding.Tests
{
    public class MemoryJournalShared : AsyncWriteProxyEx
    {
        public override TimeSpan Timeout { get; }

        public MemoryJournalShared()
        {
            Timeout = Context.System.Settings.Config.GetTimeSpan("akka.persistence.journal.memory-journal-shared.timeout");
        }

        public static void SetStore(IActorRef store, ActorSystem system)
        {
            Persistence.Persistence.Instance.Get(system).JournalFor(null).Tell(new SetStore(store));
        }
    }
}
