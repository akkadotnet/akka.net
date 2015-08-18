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
        private readonly PersistenceExtension _persistence;
        private readonly EventAdapters _eventAdapters;

        protected WriteJournalBase()
        {
            _persistence = Persistence.Instance.Apply(Context.System);
            _eventAdapters = _persistence.AdaptersFor(Self);
        }

        //protected IEnumerable<IPersistentRepresentation> CreatePersistentBatch(IEnumerable<IPersistentEnvelope> resequencables)
        //{
        //    return resequencables.Where(PreparePersistentWrite).Cast<IPersistentRepresentation>();
        //}

        protected bool PreparePersistentWrite(IPersistentEnvelope persistentEnvelope)
        {
            if (persistentEnvelope is IPersistentRepresentation)
            {
                var repr = AdaptToJournal(persistentEnvelope as IPersistentRepresentation);
                repr.PrepareWrite(Context);
                return true;
            }

            return false;
        }

        protected IEnumerable<IPersistentRepresentation> CreatePersistentBatch(IEnumerable<IPersistentEnvelope> resequencables)
        {
            return resequencables
               .Where(e => e is IPersistentRepresentation)
               .Select(e => AdaptToJournal(e as IPersistentRepresentation).PrepareWrite(Context));
        }

        protected IEnumerable<IPersistentRepresentation> AdaptFromJournal(IPersistentRepresentation representation)
        {
            return _eventAdapters.Get(representation.Payload.GetType())
                .FromJournal(representation.Payload, representation.Manifest)
                .Events
                .Select(representation.WithPayload);
        }

        protected IPersistentRepresentation AdaptToJournal(IPersistentRepresentation representation)
        {
            var payload = representation.Payload;
            var adapter = _eventAdapters.Get(payload.GetType());
            var manifest = adapter.Manifest(payload);

            return representation.WithPayload(adapter.ToJournal(payload)).WithManifest(manifest);
        }
    }
}

