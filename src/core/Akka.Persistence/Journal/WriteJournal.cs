//-----------------------------------------------------------------------
// <copyright file="WriteJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Annotations;

namespace Akka.Persistence.Journal
{
    /// <summary>
    /// Base class for the journal persistence
    /// </summary>
    public abstract class WriteJournalBase : ActorBase
    {
        private readonly EventAdapters _eventAdapters;

        /// <summary>
        /// Initializes the journal base and loads the event adapters registered for this journal plugin.
        /// </summary>
        protected WriteJournalBase()
        {
            var persistence = Persistence.Instance.Apply(Context.System);
            _eventAdapters = persistence.AdaptersFor(Self);
        }

        /// <summary>
        /// Creates a sequence of atomic writes from the supplied persistent envelopes.
        /// Applies any registered event adapters to each persistent message's payload.
        /// </summary>
        /// <param name="resequenceables">The persistent envelopes to prepare for writing.</param>
        /// <returns>
        /// A lazily generated sequence containing one adapted <see cref="AtomicWrite"/> for each
        /// input envelope that is an <see cref="AtomicWrite"/>. Other envelope types are skipped.
        /// Each persistent message is copied with its sender set to <see cref="ActorRefs.NoSender"/>
        /// before its payload is adapted. The copy retains the payload and manifest and uses the
        /// source sequence number, persistence identifier, deletion flag, and writer GUID; its
        /// timestamp is not copied. Event adapters may then change the payload or manifest.
        /// </returns>
        protected IEnumerable<AtomicWrite> PreparePersistentBatch(IEnumerable<IPersistentEnvelope> resequenceables)
        {
            foreach (var resequenceable in resequenceables)
            {
                if (resequenceable is not AtomicWrite) continue;
                
                var result = ImmutableList.CreateBuilder<IPersistentRepresentation>();

                foreach (var representation in (IEnumerable<IPersistentRepresentation>)resequenceable.Payload)
                {
                    var adapted = AdaptToJournal(representation.Update(representation.SequenceNr, representation.PersistenceId, representation.IsDeleted,
                        ActorRefs.NoSender, representation.WriterGuid));
                    result.Add(adapted);
                }
                yield return new AtomicWrite(result.ToImmutable());
            }
        }

        /// <summary>
        /// Apply registered eventadapter to the data payload
        /// </summary>
        [InternalApi]
        protected IEnumerable<IPersistentRepresentation> AdaptFromJournal(IPersistentRepresentation representation)
        {
            return _eventAdapters.Get(representation.Payload.GetType())
                .FromJournal(representation.Payload, representation.Manifest)
                .Events
                .Select(representation.WithPayload);
        }

        /// <summary>
        /// Apply any registered eventadapter to the data payload
        /// </summary>
        protected IPersistentRepresentation AdaptToJournal(IPersistentRepresentation representation)
        {
            var payload = representation.Payload;
            var adapter = _eventAdapters.Get(payload.GetType());

            // IdentityEventAdapter returns "" as manifest and normally the incoming IPersistentRepresentation
            // doesn't have an assigned manifest, but when WriteMessages is sent directly to the
            // journal for testing purposes we want to preserve the original manifest instead of
            // letting IdentityEventAdapter clearing it out.
            return (Equals(adapter, IdentityEventAdapter.Instance) || adapter is NoopWriteEventAdapter)
                ? representation
                : representation.WithPayload(adapter.ToJournal(payload)).WithManifest(adapter.Manifest(payload));
        }
    }
}

