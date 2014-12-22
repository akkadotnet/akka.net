using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;

namespace Akka.Persistence.Journal
{
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

    public class MemoryStore : ActorBase
    {
        private readonly ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>> _messages = new ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>>();

        protected override bool Receive(object message)
        {
            if (message is AsyncWriteProxy.WriteMessages) Add(message as AsyncWriteProxy.WriteMessages);
            else if (message is DeleteMessagesTo) Delete(message as DeleteMessagesTo);
            else if (message is AsyncWriteProxy.ReplayMessages) Read(message as AsyncWriteProxy.ReplayMessages);
            else if (message is AsyncWriteProxy.ReadHighestSequenceNr) GetHighestSequenceNumber(message as AsyncWriteProxy.ReadHighestSequenceNr);
            else return false;
            return true;
        }

        private void GetHighestSequenceNumber(AsyncWriteProxy.ReadHighestSequenceNr rhsn)
        {
            LinkedList<IPersistentRepresentation> list;
            Sender.Tell(_messages.TryGetValue(rhsn.PersistenceId, out list)
                ? list.Last.Value.SequenceNr
                : 0L);
        }

        private void Read(AsyncWriteProxy.ReplayMessages replay)
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

            Sender.Tell(AsyncWriteProxy.ReplaySuccess.Instance);
        }

        private void Delete(DeleteMessagesTo deleteCommand)
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

        private static void MarkAsDeleted(DeleteMessagesTo deleteCommand, LinkedListNode<IPersistentRepresentation> node)
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

        private static void DeletePermanently(DeleteMessagesTo deleteCommand, LinkedListNode<IPersistentRepresentation> node, LinkedList<IPersistentRepresentation> list)
        {
            while (node != null)
            {
                if (node.Value.SequenceNr <= deleteCommand.ToSequenceNr)
                {
                    list.Remove(node);
                }

                node = node.Next;
            }
        }

        private void Add(AsyncWriteProxy.WriteMessages writeMessages)
        {
            foreach (var persistent in writeMessages.Messages)
            {
                var list = _messages.GetOrAdd(persistent.PersistenceId, new LinkedList<IPersistentRepresentation>());
                list.AddLast(persistent);
            }

            Sender.Tell(new object());
        }
    }
}