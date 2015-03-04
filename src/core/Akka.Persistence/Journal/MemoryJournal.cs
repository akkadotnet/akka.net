using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;

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
    public class MemoryJournal : AsyncWriteProxy
    {
        private readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

        protected override void PreStart()
        {
            base.PreStart();
            Self.Tell(new SetStore(Context.ActorOf(Props.Create<MemoryStore>())));
        }
    }

    public class MemoryStore : ActorBase, IMemoryMessages
    {
        private readonly ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>> _messages = new ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>>();

        protected override bool Receive(object message)
        {
            if (message is AsyncWriteTarget.WriteMessages) Add((AsyncWriteTarget.WriteMessages)message);
            else if (message is AsyncWriteTarget.DeleteMessagesTo) Delete((AsyncWriteTarget.DeleteMessagesTo)message);
            else if (message is AsyncWriteTarget.ReplayMessages) Read((AsyncWriteTarget.ReplayMessages)message);
            else if (message is AsyncWriteTarget.ReadHighestSequenceNr) GetHighestSequenceNumber((AsyncWriteTarget.ReadHighestSequenceNr)message);
            else return false;
            return true;
        }

        private void GetHighestSequenceNumber(AsyncWriteTarget.ReadHighestSequenceNr rhsn)
        {
            LinkedList<IPersistentRepresentation> list;
            Sender.Tell(_messages.TryGetValue(rhsn.PersistenceId, out list)
                ? list.Last.Value.SequenceNr
                : 0L);
        }

        private void Read(AsyncWriteTarget.ReplayMessages replay)
        {
            LinkedList<IPersistentRepresentation> list;
            if (_messages.TryGetValue(replay.PersistenceId, out list))
            {
                var filtered = list
                    .Where(x => x.SequenceNr >= replay.FromSequenceNr && x.SequenceNr <= replay.ToSequenceNr)
                    .Take(replay.Max >= int.MaxValue ? int.MaxValue : (int)replay.Max);

                foreach (var persistent in filtered)
                {
                    Sender.Tell(persistent);
                }
            }

            Sender.Tell(AsyncWriteTarget.ReplaySuccess.Instance);
        }

        private void Delete(AsyncWriteTarget.DeleteMessagesTo deleteCommand)
        {
            LinkedList<IPersistentRepresentation> list;
            if (_messages.TryGetValue(deleteCommand.PersistenceId, out list))
            {
                var node = list.First;
                if (deleteCommand.IsPermanent)
                {
                    DeletePermanently(deleteCommand, node, list);
                }
                else
                {
                    MarkAsDeleted(deleteCommand, node);
                }
            }

            Sender.Tell(new object());
        }

        private static void MarkAsDeleted(AsyncWriteTarget.DeleteMessagesTo deleteCommand, LinkedListNode<IPersistentRepresentation> node)
        {
            while (node != null)
            {
                if (node.Value.SequenceNr <= deleteCommand.ToSequenceNr)
                {
                    var curr = node.Value;
                    node.Value = curr.Update(sequenceNr: curr.SequenceNr,
                        persistenceId: curr.PersistenceId,
                        isDeleted: true,
                        sender: curr.Sender);
                }

                node = node.Next;
            }
        }

        private static void DeletePermanently(AsyncWriteTarget.DeleteMessagesTo deleteCommand, LinkedListNode<IPersistentRepresentation> node, LinkedList<IPersistentRepresentation> list)
        {
            while (node != null)
            {
                if (node.Value.SequenceNr <= deleteCommand.ToSequenceNr)
                {
                    var deleted = node;
                    node = node.Next;

                    list.Remove(deleted);
                }
                else node = node.Next;

            }
        }

        private void Add(AsyncWriteTarget.WriteMessages writeMessages)
        {
            foreach (var persistent in writeMessages.Messages)
            {
                var list = _messages.GetOrAdd(persistent.PersistenceId, new LinkedList<IPersistentRepresentation>());
                list.AddLast(persistent);
            }

            Sender.Tell(new object());
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
                    .Take(max > int.MaxValue ? int.MaxValue : (int) max);
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