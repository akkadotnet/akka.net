using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Persistence.Journal
{
    public abstract class SyncWriteJournal : WriteJournalBase, IAsyncRecovery
    {
        private readonly PersistenceExtension _extension;
        protected readonly bool CanPublish;

        protected SyncWriteJournal()
        {
            _extension = Persistence.Instance.Apply(Context.System);
            if (_extension == null)
            {
                throw new ArgumentException("Couldn't initialize SyncWriteJournal instance, because associated Persistence extension has not been used in current actor system context.");
            }

            CanPublish = _extension.Settings.Internal.PublishPluginCommands;
        }

        public abstract Task ReplayMessagesAsync(string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> replayCallback);

        public abstract Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr);

        /// <summary>
        /// Synchronously writes a batch of a persistent messages to the journal. The batch must be atomic,
        /// i.e. all persistent messages in batch are written at once or none of them.
        /// </summary>
        protected abstract void WriteMessages(IEnumerable<IPersistentRepresentation> messages);

        /// <summary>
        /// Synchronously deletes all persistent messages up to inclusive <paramref name="toSequenceNr"/>
        /// bound. If <paramref name="isPermanent"/> flag is clear, the persistent messages are marked as
        /// deleted, otherwise they're permanently deleted.
        /// </summary>
        protected abstract void DeleteMessagesTo(string persistenceId, long toSequenceNr, bool isPermanent);

        protected override bool Receive(object message)
        {
            if (message is WriteMessages) HandleWriteMessages(message as WriteMessages);
            else if (message is ReplayMessages) HandleReplayMessages(message as ReplayMessages);
            else if (message is ReadHighestSequenceNr) HandleReadHighestSequenceNr(message as ReadHighestSequenceNr);
            else if (message is DeleteMessagesTo) HandleDeleteMessagesTo(message as DeleteMessagesTo);
            else return false;
            return true;
        }

        private void HandleDeleteMessagesTo(DeleteMessagesTo msg)
        {
            try
            {
                DeleteMessagesTo(msg.PersistenceId, msg.ToSequenceNr, msg.IsPermanent);

                if (CanPublish) Context.System.EventStream.Publish(msg);
            }
            catch (Exception e) { /* do nothing */ }
        }

        private void HandleReadHighestSequenceNr(ReadHighestSequenceNr msg)
        {
            ReadHighestSequenceNrAsync(msg.PersistenceId, msg.FromSequenceNr)
                .ContinueWith(t => t.IsFaulted
                        ? (object)new ReadHighestSequenceNrFailure(t.Exception)
                        : new ReadHighestSequenceNrSuccess(t.Result))
                .PipeTo(msg.PersistentActor);
        }

        private void HandleReplayMessages(ReplayMessages msg)
        {
            ReplayMessagesAsync(msg.PersistenceId, msg.FromSequenceNr, msg.ToSequenceNr, msg.Max, persistent =>
            {
                if (!persistent.IsDeleted || msg.ReplayDeleted) msg.PersistentActor.Tell(new ReplayedMessage(persistent), persistent.Sender);
            })
            .NotifyAboutReplayCompletion(msg.PersistentActor)
            .ContinueWith(t =>
            {
                if (!t.IsFaulted && CanPublish) Context.System.EventStream.Publish(msg);
            });
        }

        private void HandleWriteMessages(WriteMessages msg)
        {
            try
            {
                var batch = CreatePersitentBatch(msg.Messages);
                WriteMessages(batch);

                msg.PersistentActor.Tell(WriteMessagesSuccessull.Instance);
                foreach (var message in msg.Messages)
                {
                    if (message is IPersistentRepresentation)
                    {
                        var p = message as IPersistentRepresentation;
                        msg.PersistentActor.Tell(new WriteMessageSuccess(p, msg.ActorInstanceId), p.Sender);
                    }
                    else
                    {
                        msg.PersistentActor.Tell(new LoopMessageSuccess(message.Payload, msg.ActorInstanceId), message.Sender);
                    }
                }
            }
            catch (Exception e)
            {
                msg.PersistentActor.Tell(new WriteMessagesFailed(e));
                foreach (var message in msg.Messages)
                {
                    if (message is IPersistentRepresentation)
                    {
                        var p = message as IPersistentRepresentation;
                        msg.PersistentActor.Tell(new WriteMessageFailure(p, e, msg.ActorInstanceId), p.Sender);
                    }
                    else
                    {
                        msg.PersistentActor.Tell(new LoopMessageSuccess(message.Payload, msg.ActorInstanceId), message.Sender);
                    }
                }

                throw;
            }
        }
    }
}