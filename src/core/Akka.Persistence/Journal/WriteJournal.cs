using System.Collections.Generic;
using System.Linq;
using Akka.Actor;

namespace Akka.Persistence.Journal
{
    public abstract class WriteJournalBase : ActorBase
    {
        protected IEnumerable<IPersistentRepresentation> CreatePersistentBatch(IEnumerable<IPersistentEnvelope> resequencables)
        {
            return resequencables.Where(PreparePersistentWrite).Cast<IPersistentRepresentation>();
        }

        protected bool PreparePersistentWrite(IPersistentEnvelope persistentEnvelope)
        {
            if (persistentEnvelope is IPersistentRepresentation)
            {
                var repr = persistentEnvelope as IPersistentRepresentation;
                repr.PrepareWrite(Context);
                return true;
            }

            return false;
        }
    }
}