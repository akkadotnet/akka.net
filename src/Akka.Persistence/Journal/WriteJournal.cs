using System.Collections.Generic;
using System.Linq;
using Akka.Actor;

namespace Akka.Persistence.Journal
{
    public abstract class WriteJournalBase : ActorBase
    {
        protected IEnumerable<IPersistentRepresentation> CreatePersitentBatch(IEnumerable<IResequencable> resequencables)
        {
            return resequencables.Where(PreparePersitentWrite).Cast<IPersistentRepresentation>();
        }

        protected bool PreparePersitentWrite(IResequencable resequencable)
        {
            if (resequencable is IPersistentRepresentation)
            {
                var repr = resequencable as IPersistentRepresentation;
                // repr.PrepareWrite();
                return true;
            }

            return false;
        }
    }
}