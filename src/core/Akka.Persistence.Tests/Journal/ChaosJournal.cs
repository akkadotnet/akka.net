//-----------------------------------------------------------------------
// <copyright file="ChaosJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Persistence.Journal;

namespace Akka.Persistence.Tests.Journal
{
    using Messages = IDictionary<string, LinkedList<IPersistentRepresentation>>;

    internal class WriteFailedException : TestException
    {
        public WriteFailedException(IEnumerable<IPersistentRepresentation> ps)
            : base(string.Format("write failed for payloads = [{0}]", string.Join(", ", ps.Select(x => x.Payload)))) { }
    }

    internal class ReplayFailedException : TestException
    {
        public ReplayFailedException(IEnumerable<IPersistentRepresentation> ps)
            : base(string.Format("recovery failed after replaying payloads = [{0}]", string.Join(", ", ps.Select(x => x.Payload)))) { }
    }

    internal class ReadHighestFailedException : TestException
    {
        public ReadHighestFailedException() : base("recovery failed when reading the highest sequence number") { }
    }

    /*TODO: this class is not used*/public class ChaosJournal : SyncWriteJournal, IMemoryMessages
    {
        private readonly ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>> _messages = new ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>>();

        private readonly Random _random = new Random();

        private readonly double _writeFailureRate;
        private readonly double _deleteFailureRate;
        private readonly double _replayFailureRate;
        private readonly double _readHighestFailureRate;

        public ChaosJournal()
        {
            var config = Context.System.Settings.Config.GetConfig("akka.persistence.journal.chaos");
            _writeFailureRate = config.GetDouble("write-failure-rate");
            _deleteFailureRate = config.GetDouble("delete-failure-rate");
            _replayFailureRate = config.GetDouble("replay-failure-rate");
            _readHighestFailureRate = config.GetDouble("read-highest-failure-rate");
        }

        public override Task ReplayMessagesAsync(string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> replayCallback)
        {
            if (ChaosSupportExtensions.ShouldFail(_replayFailureRate))
            {
                var replays = Read(persistenceId, fromSequenceNr, toSequenceNr, max).ToArray();
                var top = replays.Take(_random.Next(replays.Length + 1));
                foreach (var persistentRepresentation in top)
                {
                    replayCallback(persistentRepresentation);
                }

                return Task.FromResult(0L).ContinueWith(task => { throw new ReplayFailedException(top); });
            }
            else
            {
                foreach (var p in Read(persistenceId, fromSequenceNr, toSequenceNr, max))
                {
                    replayCallback(p);
                }
                return Task.FromResult(new object());
            }
        }

        public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            if (ChaosSupportExtensions.ShouldFail(_readHighestFailureRate))
                return Task.FromResult(0L).ContinueWith(task => { throw new ReadHighestFailedException(); return 0L; });
            else
                return Task.FromResult(HighestSequenceNr(persistenceId));
        }

        public override void WriteMessages(IEnumerable<IPersistentRepresentation> messages)
        {
            if (ChaosSupportExtensions.ShouldFail(_writeFailureRate))
                throw new WriteFailedException(messages);
            else
            {
                foreach (var message in messages)
                {
                    Add(message);
                }
            }
        }

        public override void DeleteMessagesTo(string persistenceId, long toSequenceNr, bool isPermanent)
        {
            foreach (var sequenceNr in Enumerable.Range(1, (int)toSequenceNr))
            {
                if (isPermanent)
                    Update(persistenceId, sequenceNr, x => x.Update(x.SequenceNr, x.PersistenceId, true, x.Sender));
                else Delete(persistenceId, sequenceNr);
            }
        }

        #region IMemoryMessages implementation

        public Messages Add(IPersistentRepresentation persistent)
        {
            var list = _messages.GetOrAdd(persistent.PersistenceId, new LinkedList<IPersistentRepresentation>());
            list.AddLast(persistent);
            return _messages;
        }

        public Messages Update(string pid, long seqNr, Func<IPersistentRepresentation, IPersistentRepresentation> updater)
        {
            LinkedList<IPersistentRepresentation> persistents;
            if (_messages.TryGetValue(pid, out persistents))
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
            if (_messages.TryGetValue(pid, out persistents))
            {
                var node = persistents.First;
                while (node != null)
                {
                    if (node.Value.SequenceNr == seqNr)
                        persistents.Remove(node);

                    node = node.Next;
                }
            }

            return _messages;
        }

        public IEnumerable<IPersistentRepresentation> Read(string pid, long fromSeqNr, long toSeqNr, long max)
        {
            LinkedList<IPersistentRepresentation> persistents;
            if (_messages.TryGetValue(pid, out persistents))
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
            if (_messages.TryGetValue(pid, out persistents))
            {
                var last = persistents.LastOrDefault();
                return last != null ? last.SequenceNr : 0L;
            }

            return 0L;
        }

        #endregion
    }
}

