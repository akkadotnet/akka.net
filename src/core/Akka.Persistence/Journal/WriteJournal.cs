//-----------------------------------------------------------------------
// <copyright file="WriteJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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

