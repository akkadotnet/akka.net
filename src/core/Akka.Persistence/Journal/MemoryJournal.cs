//-----------------------------------------------------------------------
// <copyright file="MemoryJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

    /// <summary>
    /// TBD
    /// </summary>
    public interface IMemoryMessages
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistent">TBD</param>
        /// <returns>TBD</returns>
        Messages Add(IPersistentRepresentation persistent);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="pid">TBD</param>
        /// <param name="seqNr">TBD</param>
        /// <param name="updater">TBD</param>
        /// <returns>TBD</returns>
        Messages Update(string pid, long seqNr, Func<IPersistentRepresentation, IPersistentRepresentation> updater);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="pid">TBD</param>
        /// <param name="seqNr">TBD</param>
        /// <returns>TBD</returns>
        Messages Delete(string pid, long seqNr);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="pid">TBD</param>
        /// <param name="fromSeqNr">TBD</param>
        /// <param name="toSeqNr">TBD</param>
        /// <param name="max">TBD</param>
        /// <returns>TBD</returns>
        IEnumerable<IPersistentRepresentation> Read(string pid, long fromSeqNr, long toSeqNr, long max);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="pid">TBD</param>
        /// <returns>TBD</returns>
        long HighestSequenceNr(string pid);
    }

    /// <summary>
    /// In-memory journal for testing purposes.
    /// </summary>
    public class MemoryJournal : AsyncWriteJournal
    {
        private readonly ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>> _messages = new ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>>();
        private readonly ConcurrentDictionary<string, long> _meta = new ConcurrentDictionary<string, long>();

        /// <summary>
        /// TBD
        /// </summary>
        protected virtual ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>> Messages { get { return _messages; } }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="messages">TBD</param>
        /// <returns>TBD</returns>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistenceId">TBD</param>
        /// <param name="fromSequenceNr">TBD</param>
        /// <returns>TBD</returns>
        public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            return Task.FromResult(Math.Max(HighestSequenceNr(persistenceId), _meta.TryGetValue(persistenceId, out long metaSeqNr) ? metaSeqNr : 0L));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="persistenceId">TBD</param>
        /// <param name="fromSequenceNr">TBD</param>
        /// <param name="toSequenceNr">TBD</param>
        /// <param name="max">TBD</param>
        /// <param name="recoveryCallback">TBD</param>
        /// <returns>TBD</returns>
        public override Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max,
            Action<IPersistentRepresentation> recoveryCallback)
        {
            var highest = HighestSequenceNr(persistenceId);
            if (highest != 0L && max != 0L)
                Read(persistenceId, fromSequenceNr, Math.Min(toSequenceNr, highest), max).ForEach(recoveryCallback);
            return Task.FromResult(new object());
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistenceId">TBD</param>
        /// <param name="toSequenceNr">TBD</param>
        /// <returns>TBD</returns>
        protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
        {
            var highestSeqNr = HighestSequenceNr(persistenceId);
            var toSeqNr = Math.Min(toSequenceNr, highestSeqNr);
            if (toSeqNr == highestSeqNr)
                _meta.AddOrUpdate(persistenceId, highestSeqNr, (pid, old) => highestSeqNr);
            for (var snr = 1L; snr <= toSeqNr; snr++)
                Delete(persistenceId, snr);
            return Task.FromResult(new object());
        }

        #region IMemoryMessages implementation

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistent">TBD</param>
        /// <returns>TBD</returns>
        public Messages Add(IPersistentRepresentation persistent)
        {
            var list = Messages.GetOrAdd(persistent.PersistenceId, pid => new LinkedList<IPersistentRepresentation>());
            list.AddLast(persistent);
            return Messages;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="pid">TBD</param>
        /// <param name="seqNr">TBD</param>
        /// <param name="updater">TBD</param>
        /// <returns>TBD</returns>
        public Messages Update(string pid, long seqNr, Func<IPersistentRepresentation, IPersistentRepresentation> updater)
        {
            if (Messages.TryGetValue(pid, out LinkedList<IPersistentRepresentation> persistents))
            {
                var node = persistents.First;
                while (node != null)
                {
                    if (node.Value.SequenceNr == seqNr)
                        node.Value = updater(node.Value);

                    node = node.Next;
                }
            }

            return Messages;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="pid">TBD</param>
        /// <param name="seqNr">TBD</param>
        /// <returns>TBD</returns>
        public Messages Delete(string pid, long seqNr)
        {
            if (Messages.TryGetValue(pid, out LinkedList<IPersistentRepresentation> persistents))
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="pid">TBD</param>
        /// <param name="fromSeqNr">TBD</param>
        /// <param name="toSeqNr">TBD</param>
        /// <param name="max">TBD</param>
        /// <returns>TBD</returns>
        public IEnumerable<IPersistentRepresentation> Read(string pid, long fromSeqNr, long toSeqNr, long max)
        {
            if (Messages.TryGetValue(pid, out LinkedList<IPersistentRepresentation> persistents))
            {
                return persistents
                    .Where(x => x.SequenceNr >= fromSeqNr && x.SequenceNr <= toSeqNr)
                    .Take(max > int.MaxValue ? int.MaxValue : (int)max);
            }

            return Enumerable.Empty<IPersistentRepresentation>();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="pid">TBD</param>
        /// <returns>TBD</returns>
        public long HighestSequenceNr(string pid)
        {
            if (Messages.TryGetValue(pid, out LinkedList<IPersistentRepresentation> persistents))
            {
                var last = persistents.LastOrDefault();
                return last?.SequenceNr ?? 0L;
            }

            return 0L;
        }

        #endregion
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class SharedMemoryJournal : MemoryJournal
    {
        private static readonly ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>> SharedMessages = new ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>>();

        /// <summary>
        /// TBD
        /// </summary>
        protected override ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>> Messages { get { return SharedMessages; } }
    }
}

