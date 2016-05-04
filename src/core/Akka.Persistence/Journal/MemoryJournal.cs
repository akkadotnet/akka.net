//-----------------------------------------------------------------------
// <copyright file="MemoryJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Util.Internal;

namespace Akka.Persistence.Journal
{
    using Messages = IDictionary<string, LinkedList<IPersistentRepresentation>>;

    public interface IMemoryMessages
    {
        Messages Add(IPersistentRepresentation persistent);
        Messages Update(string pid, long seqNr, Func<IPersistentRepresentation, IPersistentRepresentation> updater);
        Messages Delete(string pid, long seqNr);
        IEnumerable<IPersistentRepresentation> Read(string pid, long fromSeqNr, long toSeqNr, long max);
        long HighestSequenceNr(string pid);
    }

    /// <summary>
    /// In-memory journal for testing purposes.
    /// </summary>
    public class MemoryJournal : AsyncWriteJournal
    {
        private readonly ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>> _messages = new ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>>();

        protected virtual ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>> Messages { get { return _messages; } }

        protected override Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages)
        {
            foreach (var w in messages)
            {
                foreach (var p in (IEnumerable<IPersistentRepresentation>)w.Payload)
                {
                    Add(p);
                }
            }
            return Task.FromResult((IImmutableList<Exception>) null); // all good
        }

        public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            return Task.FromResult(HighestSequenceNr(persistenceId));
        }

        public override Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max,
            Action<IPersistentRepresentation> recoveryCallback)
        {
            var highest = HighestSequenceNr(persistenceId);
            if (highest != 0L && max != 0L)
                Read(persistenceId, fromSequenceNr, Math.Min(toSequenceNr, highest), max).ForEach(recoveryCallback);
            return Task.FromResult(new object());
        }

        protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
        {
            var toSeqNr = Math.Min(toSequenceNr, HighestSequenceNr(persistenceId));
            for (var snr = 1L; snr <= toSeqNr; snr++)
                Delete(persistenceId, snr);
            return Task.FromResult(new object());
        }

        #region IMemoryMessages implementation

        public Messages Add(IPersistentRepresentation persistent)
        {
            var list = Messages.GetOrAdd(persistent.PersistenceId, pid => new LinkedList<IPersistentRepresentation>());
            list.AddLast(persistent);
            return Messages;
        }

        public Messages Update(string pid, long seqNr, Func<IPersistentRepresentation, IPersistentRepresentation> updater)
        {
            LinkedList<IPersistentRepresentation> persistents;
            if (Messages.TryGetValue(pid, out persistents))
            {
                var node = persistents.First;
                while (node != null)
                {
                    if (node.Value.SequenceNr == seqNr)
                        node.Value = updater(node.Value);

                    node = node.Next;
                }
            }

            return _messages;
        }

        public Messages Delete(string pid, long seqNr)
        {
            LinkedList<IPersistentRepresentation> persistents;
            if (Messages.TryGetValue(pid, out persistents))
            {
                var node = persistents.First;
                while (node != null)
                {
                    if (node.Value.SequenceNr == seqNr)
                        persistents.Remove(node);

                    node = node.Next;
                }
            }

            return Messages;
        }

        public IEnumerable<IPersistentRepresentation> Read(string pid, long fromSeqNr, long toSeqNr, long max)
        {
            LinkedList<IPersistentRepresentation> persistents;
            if (Messages.TryGetValue(pid, out persistents))
            {
                return persistents
                    .Where(x => x.SequenceNr >= fromSeqNr && x.SequenceNr <= toSeqNr)
                    .Take(max > int.MaxValue ? int.MaxValue : (int)max);
            }

            return Enumerable.Empty<IPersistentRepresentation>();
        }

        public long HighestSequenceNr(string pid)
        {
            LinkedList<IPersistentRepresentation> persistents;
            if (Messages.TryGetValue(pid, out persistents))
            {
                var last = persistents.LastOrDefault();
                return last != null ? last.SequenceNr : 0L;
            }

            return 0L;
        }

        #endregion
    }

    public class SharedMemoryJournal : MemoryJournal
    {
        private static readonly ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>> SharedMessages = new ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>>();

        protected override ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>> Messages { get { return SharedMessages; } }
    }
}

