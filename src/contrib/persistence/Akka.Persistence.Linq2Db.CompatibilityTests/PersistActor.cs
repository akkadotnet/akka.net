using System.Collections.Generic;
using System.Linq;
using Akka.Actor;

namespace Akka.Persistence.Linq2Db.CompatibilityTests
{
    public class PersistActor : ReceivePersistentActor
    {
        private List<SomeEvent> events = new List<SomeEvent>();
        public PersistActor(string journal, string persistenceId)
        {
            JournalPluginId = journal;
            PersistenceId = persistenceId;
            Command<SomeEvent>(se=>Persist(se, p =>
            {
                events.Add(p);
            }));
            Command<ContainsEvent>(ce=>Context.Sender.Tell(events.Any(e=>e.Guid==ce.Guid)));
            Recover<SomeEvent>(se => events.Add(se));
        }
        public override string PersistenceId { get; }
    }
}