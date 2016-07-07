//-----------------------------------------------------------------------
// <copyright file="WriteJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
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

        protected IEnumerable<AtomicWrite> PreparePersistentBatch(IEnumerable<IPersistentEnvelope> resequencables)
        {
            return resequencables
               .OfType<AtomicWrite>()
               .Select(aw => new AtomicWrite(((IEnumerable<IPersistentRepresentation>)aw.Payload).Select(p => AdaptToJournal(p.Update(p.SequenceNr, p.PersistenceId, p.IsDeleted, ActorRefs.NoSender, p.WriterGuid))).ToImmutableList()));
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
            representation = representation.WithPayload(adapter.ToJournal(payload));

            // IdentityEventAdapter returns "" as manifest and normally the incoming PersistentRepr
            // doesn't have an assigned manifest, but when WriteMessages is sent directly to the
            // journal for testing purposes we want to preserve the original manifest instead of
            // letting IdentityEventAdapter clearing it out.
            return Equals(adapter, IdentityEventAdapter.Instance) 
                ? representation
                : representation.WithManifest(adapter.Manifest(payload));
        }
    }
}

