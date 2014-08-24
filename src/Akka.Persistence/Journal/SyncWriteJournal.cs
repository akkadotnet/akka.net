using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Akka.Persistence.Journal
{
    public abstract class SyncWriteJournal: WriteJournalBase, IAsyncRecovery
    {
        private readonly Persistence _persistence;
        private readonly bool _publish;

        protected SyncWriteJournal()
        {
            _persistence = Context.System.GetExtension<Persistence>();
            if (_persistence == null)
            {
                throw new ArgumentException("Couldn't initialize SyncWriteJournal instance, because associated Persistance extension has not been used in current actor system context.");
            }

            _publish = _persistence.Settings.Internal.PublishPluginCommands;
        }

        protected override bool Receive(object message)
        {
            if (message is WriteMessages)
            {
                HandleWriteMessages((WriteMessages)message);
            }
            else if (message is ReplayMessages)
            {
                HandleReplayMessages((ReplayMessages)message);
            }
            else if (message is ReadHighestSequenceNr)
            {
                HandleReadHighestSequenceNr((ReadHighestSequenceNr)message);
            }
            else if (message is WriteConfirmations)
            {
                HandleWriteConfirmations((WriteConfirmations)message);
            }
            else if (message is DeleteMessages)
            {
                HandleDeleteMessages((DeleteMessages)message);
            }
            else if (message is DeleteMessagesTo)
            {
                HandleDeleteMessagesTo((DeleteMessagesTo)message);
            }
            else if (message is LoopMessage)
            {
                var msg = (LoopMessage) message;
                msg.PersistentActor.Forward(new LoopMessageSuccess(msg.Message, msg.ActorInstanceId));
            }
            else
            {
                return false;
            }

            return true;
        }

        public abstract Task ReplayMessagesAsync(string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> replayCallback);
        public abstract Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr);
        protected abstract void WriteMessages(IEnumerable<IPersistentRepresentation> messages);
        protected abstract void WriteConfirmations(IEnumerable<IPersistentConfirmation> confirmations);
        protected abstract void DeleteMessages(IEnumerable<IPersistentId> messageIds, bool isPermanent);
        protected abstract void DeleteMessagesTo(string persistenceId, long toSequenceNr, bool isPermanent);

        private void HandleDeleteMessagesTo(DeleteMessagesTo msg)
        {
            try
            {
                DeleteMessagesTo(msg.PersistenceId, msg.ToSequenceNr, msg.IsPermanent);
                if (_publish)
                {
                    Context.System.EventStream.Publish(msg);
                }
            }
            catch (Exception e)
            {
                /* do nothing */
            }
        }

        private void HandleDeleteMessages(DeleteMessages msg)
        {
            try
            {
                DeleteMessages(msg.MessageIds, msg.IsPermanent);
                if (msg.Requestor != null)
                {
                    msg.Requestor.Tell(new DeleteMessagesSuccess(msg.MessageIds));
                }

                if (_publish)
                {
                    Context.System.EventStream.Publish(msg);
                }
            }
            catch (Exception e)
            {
                if (msg.Requestor != null)
                {
                    msg.Requestor.Tell(new DeleteMessagesFailure(e));
                }
            }
        }

        private void HandleWriteConfirmations(WriteConfirmations msg)
        {
            try
            {
                WriteConfirmations(msg.Confirmations);
                msg.Requestor.Tell(new WriteConfirmationsSuccess(msg.Confirmations));
            }
            catch (Exception e)
            {
                msg.Requestor.Tell(new WriteConfirmationsFailure(e));
            }
        }

        private void HandleReadHighestSequenceNr(ReadHighestSequenceNr msg)
        {
            ReadHighestSequenceNrAsync(msg.PersistenceId, msg.FromSequenceNr)
                .ContinueWith(t =>
                {
                    var result = t.IsFaulted
                        ? (object)new ReadHighestSequenceNrSuccess(t.Result)
                        : new ReadHighestSequenceNrFailure(t.Exception);
                    msg.PersistentActor.Tell(result);
                });
        }

        private void HandleReplayMessages(ReplayMessages msg)
        {
            ReplayMessagesAsync(msg.PersistenceId, msg.FromSequenceNr, msg.ToSequenceNr, msg.Max, persitent =>
            {
                try
                {
                    if (!persitent.IsDeleted || msg.ReplayDeleted)
                    {
                        msg.PersistentActor.Tell(new ReplayedMessage(persitent), persitent.Sender);
                    }
                    if (_publish)
                    {
                        Context.System.EventStream.Publish(msg);
                    }

                    msg.PersistentActor.Tell(ReplayMessageSuccess.Instance);
                }
                catch (Exception e)
                {
                    msg.PersistentActor.Tell(new ReplayMessagesFailure(e));
                }
            });
        }

        private void HandleWriteMessages(WriteMessages msg)
        {
            try
            {
                var batch = CreatePersitentBatch(msg.Messages);
                WriteMessages(batch);

                msg.PersistentActor.Tell(WriteMessagesSuccess.Instance);
                foreach (var resequencable in msg.Messages)
                {
                    if (resequencable is IPersistentRepresentation)
                    {
                        var p = resequencable as IPersistentRepresentation;
                        msg.PersistentActor.Tell(new WriteMessageSuccess(p, msg.ActorInstanceId), p.Sender);
                    }
                    else
                    {
                        msg.PersistentActor.Tell(new LoopMessageSuccess(resequencable.Payload, msg.ActorInstanceId),
                            resequencable.Sender);
                    }
                }
            }
            catch (Exception e)
            {
                msg.PersistentActor.Tell(new WriteMessagesFailure(e));
                foreach (var resequencable in msg.Messages)
                {
                    if (resequencable is IPersistentRepresentation)
                    {
                        var p = resequencable as IPersistentRepresentation;
                        msg.PersistentActor.Tell(new WriteMessageFailure(p, e, msg.ActorInstanceId), p.Sender);
                    }
                    else
                    {
                        msg.PersistentActor.Tell(new LoopMessageSuccess(resequencable.Payload, msg.ActorInstanceId),
                            resequencable.Sender);
                    }
                }
                throw;
            }
        }
    }
}