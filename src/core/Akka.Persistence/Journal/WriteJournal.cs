using System.Collections.Generic;
using System.Linq;
using Akka.Actor;

namespace Akka.Persistence.Journal
{
    public abstract class WriteJournalBase : ActorBase
    {
        protected IEnumerable<IPersistentRepresentation> CreatePersitentBatch(IEnumerable<IPersistentEnvelope> resequencables)
        {
            return resequencables.Where(PreparePersitentWrite).Cast<IPersistentRepresentation>();
        }

        protected bool PreparePersitentWrite(IPersistentEnvelope persistentEnvelope)
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