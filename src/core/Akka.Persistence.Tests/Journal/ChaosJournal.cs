//-----------------------------------------------------------------------
// <copyright file="ChaosJournal.cs" company="Akka.NET Project">
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
using Akka.Persistence.Journal;
using Akka.Configuration;

namespace Akka.Persistence.Tests.Journal
{
    using Messages = IDictionary<string, LinkedList<IPersistentRepresentation>>;

    internal class WriteFailedException : TestException
    {
        public WriteFailedException(IEnumerable<AtomicWrite> ps)
            : base(string.Format("write failed for payloads = [{0}]", string.Join(", ", ps.SelectMany(x => (IEnumerable<IPersistentRepresentation>)x.Payload)))) { }
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

    public class ChaosJournal : AsyncWriteJournal, IMemoryMessages
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
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<ChaosJournal>("akka.persistence.journal.chaos");

            _writeFailureRate = config.GetDouble("write-failure-rate", 0);
            _deleteFailureRate = config.GetDouble("delete-failure-rate", 0);
            _replayFailureRate = config.GetDouble("replay-failure-rate", 0);
            _readHighestFailureRate = config.GetDouble("read-highest-failure-rate", 0);
        }

        public override Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> recoveryCallback)
        {
            var promise = new TaskCompletionSource<object>();
            if (ChaosSupportExtensions.ShouldFail(_replayFailureRate))
            {
                var replays = Read(persistenceId, fromSequenceNr, toSequenceNr, max).ToArray();
                var top = replays.Take(_random.Next(replays.Length + 1)).ToArray();
                foreach (var persistentRepresentation in top)
                {
                    recoveryCallback(persistentRepresentation);
                }
                promise.SetException(new ReplayFailedException(top));
            }
            else
            {
                foreach (var p in Read(persistenceId, fromSequenceNr, toSequenceNr, max))
                {
                    recoveryCallback(p);
                }
                promise.SetResult(new object());
            }
            return promise.Task;
        }

        public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            var promise = new TaskCompletionSource<long>();
            if (ChaosSupportExtensions.ShouldFail(_readHighestFailureRate))
                promise.SetException(new ReadHighestFailedException());
            else
                promise.SetResult(HighestSequenceNr(persistenceId));
            return promise.Task;
        }

        protected override Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages)
        {
            TaskCompletionSource<IImmutableList<Exception>> promise =
                new TaskCompletionSource<IImmutableList<Exception>>();
            if (ChaosSupportExtensions.ShouldFail(_writeFailureRate))
                promise.SetException(new WriteFailedException(messages));
            else
            {
                try
                {
                    foreach (var message in messages)
                    {
                        foreach (var persistent in (IEnumerable<IPersistentRepresentation>) message.Payload)
                        {
                            Add(persistent);
                        }
                    }
                    promise.SetResult(null);
                }
                catch (Exception e)
                {
                    promise.SetException(e);
                }
            }
            return promise.Task;
        }

        protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
        {
            TaskCompletionSource<object> promise = new TaskCompletionSource<object>();
            try
            {
                foreach (var sequenceNr in Enumerable.Range(1, (int) toSequenceNr))
                {
                    Delete(persistenceId, sequenceNr);
                }
                promise.SetResult(new object());
            }
            catch (Exception e)
            {
                promise.SetException(e);
            }
            return promise.Task;
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
            if (_messages.TryGetValue(pid, out var persistents))
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
            if (_messages.TryGetValue(pid, out var persistents))
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
            if (_messages.TryGetValue(pid, out var persistents))
            {
                return persistents
                    .Where(x => x.SequenceNr >= fromSeqNr && x.SequenceNr <= toSeqNr)
                    .Take(max > int.MaxValue ? int.MaxValue : (int)max);
            }

            return Enumerable.Empty<IPersistentRepresentation>();
        }

        public long HighestSequenceNr(string pid)
        {
            if (_messages.TryGetValue(pid, out var persistents))
            {
                var last = persistents.LastOrDefault();
                return last?.SequenceNr ?? 0L;
            }

            return 0L;
        }

        #endregion
    }
}

