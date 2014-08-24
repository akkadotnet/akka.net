using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Persistence.Journal
{
    public abstract class AsyncWriteJournal : WriteJournalBase, IAsyncRecovery
    {
        private readonly Persistence _persistence;
        private readonly bool _publish;
        private readonly ActorRef _resequencer;

        private long _resequencerCounter = 0L;

        protected AsyncWriteJournal()
        {
            _persistence = Context.System.GetExtension<Persistence>();
            if (_persistence == null)
            {
                throw new ArgumentException("Couldn't initialize SyncWriteJournal instance, because associated Persistance extension has not been used in current actor system context.");
            }

            _publish = _persistence.Settings.Internal.PublishPluginCommands;
            _resequencer = Context.System.ActorOf(Props.Create(() => new Resequencer()));
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
                var msg = (LoopMessage)message;
                _resequencer.Tell(new Desequenced(new LoopMessageSuccess(msg.Message, msg.ActorInstanceId), _resequencerCounter, msg.PersistentActor, Sender));
                Interlocked.Increment(ref _resequencerCounter);
            }
            else
            {
                return false;
            }

            return true;
        }

        public abstract Task ReplayMessagesAsync(string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> replayCallback);
        public abstract Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr);
        protected abstract Task WriteMessagesAsync(IEnumerable<IPersistentRepresentation> messages);
        protected abstract Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr, bool isPermanent);

        private void HandleDeleteMessagesTo(DeleteMessagesTo message)
        {
            DeleteMessagesToAsync(message.PersistenceId, message.ToSequenceNr, message.IsPermanent)
                .ContinueWith(t =>
                {
                    if (!t.IsFaulted && _publish)
                    {
                        Context.System.EventStream.Publish(message);
                    }
                });
        }

        private void HandleDeleteMessages(DeleteMessages message)
        {
            throw new NotImplementedException();
        }

        private void HandleWriteConfirmations(WriteConfirmations message)
        {
            throw new NotImplementedException();
        }

        private void HandleReadHighestSequenceNr(ReadHighestSequenceNr message)
        {
            ReadHighestSequenceNrAsync(message.PersistenceId, message.FromSequenceNr)
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        message.PersistentActor.Tell(new ReadHighestSequenceNrFailure(t.Exception));
                    }
                    else
                    {
                        message.PersistentActor.Tell(new ReadHighestSequenceNrSuccess(t.Result));
                    }
                });
        }

        private void HandleReplayMessages(ReplayMessages message)
        {
            ReplayMessagesAsync(message.PersistenceId, message.FromSequenceNr, message.ToSequenceNr, message.Max, p =>
            {
                try
                {
                    if (!p.IsDeleted || message.ReplayDeleted)
                    {
                        message.PersistentActor.Tell(new ReplayedMessage(p), p.Sender);
                    }
                    message.PersistentActor.Tell(ReplayMessageSuccess.Instance);
                }
                catch (Exception e)
                {
                    message.PersistentActor.Tell(new ReplayMessagesFailure(e));
                }

                if (_publish)
                {
                    Context.System.EventStream.Publish(message);    
                }
            });
        }

        private void HandleWriteMessages(WriteMessages message)
        {
            var counter = _resequencerCounter;
            WriteMessagesAsync(CreatePersitentBatch(message.Messages)).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _resequencer.Tell(new Desequenced(new WriteMessagesFailure(t.Exception), counter, message.PersistentActor, Self));
                    Resequence(message, counter, x => new WriteMessageFailure(x, t.Exception, message.ActorInstanceId));
                }
                else
                {
                    _resequencer.Tell(new Desequenced(WriteMessagesSuccess.Instance, counter, message.PersistentActor, Self));
                    Resequence(message, counter, x => new WriteMessageSuccess(x, message.ActorInstanceId));
                }
            });
            var resequencablesLength = message.Messages.Count();
            _resequencerCounter += resequencablesLength + 1;
        }

        private void Resequence(WriteMessages message, long counter, Func<IPersistentRepresentation, object> mapper)
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
                    _resequencer.Tell(new Desequenced(loopMsg, counter + i + 1, message.PersistentActor, resequencable.Sender));
                }
                i++;
            }
        }

        internal struct Desequenced
        {
            public Desequenced(object message, long sequenceNr, ActorRef target, ActorRef sender)
                : this()
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
                if (message is Desequenced)
                {
                    Desequenced? d = (Desequenced)message;
                    while (d.HasValue)
                    {
                        d = Resequence(d.Value);
                    }

                    return true;
                }

                return false;
            }

            private Desequenced? Resequence(Desequenced desequenced)
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