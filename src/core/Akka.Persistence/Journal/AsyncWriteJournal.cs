using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Persistence.Journal
{
    public abstract class AsyncWriteJournal : WriteJournalBase, IAsyncRecovery
    {
        private static readonly TaskContinuationOptions _continuationOptions = TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.AttachedToParent;
        protected readonly bool CanPublish;
        private readonly PersistenceExtension _extension;
        private readonly ActorRef _resequencer;

        private long _resequencerCounter = 1L;

        protected AsyncWriteJournal()
        {
            _extension = Persistence.Instance.Apply(Context.System);
            if (_extension == null)
            {
                throw new ArgumentException("Couldn't initialize SyncWriteJournal instance, because associated Persistance extension has not been used in current actor system context.");
            }

            CanPublish = _extension.Settings.Internal.PublishPluginCommands;
            _resequencer = Context.System.ActorOf(Props.Create(() => new Resequencer()));
        }

        public abstract Task ReplayMessagesAsync(string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> replayCallback);

        public abstract Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr);

        /// <summary>
        /// Asynchronously writes a batch of a persistent messages to the journal. The batch must be atomic,
        /// i.e. all persistent messages in batch are written at once or none of them.
        /// </summary>
        protected abstract Task WriteMessagesAsync(IEnumerable<IPersistentRepresentation> messages);

        /// <summary>
        /// Asynchronously deletes all persistent messages up to inclusive <paramref name="toSequenceNr"/>
        /// bound. If <paramref name="isPermanent"/> flag is clear, the persistent messages are marked as
        /// deleted, otherwise they're permanently deleted.
        /// </summary>
        protected abstract Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr, bool isPermanent);

        protected override bool Receive(object message)
        {
            if (message is WriteMessages) HandleWriteMessages((WriteMessages)message);
            else if (message is ReplayMessages) HandleReplayMessages((ReplayMessages)message);
            else if (message is ReadHighestSequenceNr) HandleReadHighestSequenceNr((ReadHighestSequenceNr)message);
            else if (message is DeleteMessagesTo) HandleDeleteMessagesTo((DeleteMessagesTo)message);
            else return false;
            return true;
        }

        private void HandleDeleteMessagesTo(DeleteMessagesTo message)
        {
            DeleteMessagesToAsync(message.PersistenceId, message.ToSequenceNr, message.IsPermanent)
                .ContinueWith(t =>
                {
                    if (!t.IsFaulted && CanPublish) Context.System.EventStream.Publish(message);
                }, _continuationOptions);
        }

        private void HandleReadHighestSequenceNr(ReadHighestSequenceNr message)
        {
            // Send read highest sequence number to persistentActor directly. No need
            // to resequence the result relative to written and looped messages.
            ReadHighestSequenceNrAsync(message.PersistenceId, message.FromSequenceNr)
                .ContinueWith(t => t.IsFaulted
                    ? (object)new ReadHighestSequenceNrFailure(t.Exception)
                    : new ReadHighestSequenceNrSuccess(t.Result))
                .PipeTo(message.PersistentActor);
        }

        private void HandleReplayMessages(ReplayMessages message)
        {
            // Send replayed messages and replay result to persistentActor directly. No need
            // to resequence replayed messages relative to written and looped messages.
            ReplayMessagesAsync(message.PersistenceId, message.FromSequenceNr, message.ToSequenceNr, message.Max, p =>
            {
                if (!p.IsDeleted || message.ReplayDeleted) message.PersistentActor.Tell(new ReplayedMessage(p), p.Sender);
            })
            .NotifyAboutReplayCompletion(message.PersistentActor)
            .ContinueWith(t =>
            {
                if(!t.IsFaulted && CanPublish) Context.System.EventStream.Publish(message);
            }, _continuationOptions);
        }

        private void HandleWriteMessages(WriteMessages message)
        {
            var counter = _resequencerCounter;
            Action<Func<IPersistentRepresentation, object>> resequence = (mapper) =>
            {
                var i = 0;
                foreach (var resequencable in message.Messages)
                {
                    if (resequencable is IPersistentRepresentation)
                    {
                        var p = resequencable as IPersistentRepresentation;
                        _resequencer.Tell(new Desequenced(mapper(p), counter + i + 1, message.PersistentActor, p.Sender));
                    }
                    else
                    {
                        var loopMsg = new LoopMessageSuccess(resequencable.Payload, message.ActorInstanceId);
                        _resequencer.Tell(new Desequenced(loopMsg, counter + i + 1, message.PersistentActor,
                            resequencable.Sender));
                    }
                    i++;
                }
            };

            WriteMessagesAsync(CreatePersitentBatch(message.Messages)).ContinueWith(t =>
            {
                if (!t.IsFaulted)
                {
                    _resequencer.Tell(new Desequenced(WriteMessagesSuccessull.Instance, counter, message.PersistentActor, Self));
                    resequence(x => new WriteMessageSuccess(x, message.ActorInstanceId));
                }
                else
                {
                    _resequencer.Tell(new Desequenced(new WriteMessagesFailed(t.Exception), counter, message.PersistentActor, Self));
                    resequence(x => new WriteMessageFailure(x, t.Exception, message.ActorInstanceId));
                }
            }, _continuationOptions);
            var resequencablesLength = message.Messages.Count();
            _resequencerCounter += resequencablesLength + 1;
        }

        internal sealed class Desequenced
        {
            public Desequenced(object message, long sequenceNr, ActorRef target, ActorRef sender)
            {
                Message = message;
                SequenceNr = sequenceNr;
                Target = target;
                Sender = sender;
            }

            public object Message { get; private set; }
            public long SequenceNr { get; private set; }
            public ActorRef Target { get; private set; }
            public ActorRef Sender { get; private set; }
        }

        internal class Resequencer : ActorBase
        {
            private readonly IDictionary<long, Desequenced> _delayed = new Dictionary<long, Desequenced>();
            private long _delivered = 0L;

            protected override bool Receive(object message)
            {
                Desequenced d;
                if ((d = message as Desequenced) != null)
                {
                    do
                    {
                        d = Resequence(d);
                    } while (d != null);
                    return true;
                }
                return false;
            }

            private Desequenced Resequence(Desequenced desequenced)
            {
                if (desequenced.SequenceNr == _delivered + 1)
                {
                    _delivered = desequenced.SequenceNr;
                    desequenced.Target.Tell(desequenced.Message, desequenced.Sender);
                }
                else
                {
                    _delayed.Add(desequenced.SequenceNr, desequenced);
                }

                Desequenced d;
                var delivered = _delivered + 1;
                if (_delayed.TryGetValue(delivered, out d))
                {
                    _delayed.Remove(delivered);
                    return d;
                }

                return null;
            }
        }
    }
}