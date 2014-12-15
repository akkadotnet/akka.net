using System;
using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Persistence
{
    internal interface IProcessor
    {
        State CurrentState { get; }
    }

    /// <summary>
    /// Class representing internal state, in which actor journals and processes new messages, both persistent and transient.
    /// </summary>
    internal class ProcessingState : State
    {
        private bool _batching;

        public ProcessingState(PersistentActorBase actor)
            : base(actor)
        {
            Actor.ProcessorBatch = new List<IResequencable>();
        }

        public bool IsMaxSizeReached
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override void AroundReceive(Receive receive, object message)
        {
            if (message is Recover) { }
            else if (message is ReplayedMessage)
            {
                var msg = (ReplayedMessage)message;
                ProcessPersistent(receive, msg.Persistent);
            }
            else if (message is WriteMessageSuccess)
            {
                var msg = (WriteMessageSuccess)message;
                ProcessPersistent(receive, msg.Persistent);
            }
            else if (message is WriteMessageFailure)
            {
                var msg = (WriteMessageFailure)message;
                Process(receive, new PersistenceFailure(msg.Persistent.Payload, msg.ActorInstanceId, msg.Cause));
            }
            else if (message is LoopMessageSuccess)
            {
                var msg = (LoopMessageSuccess)message;
                Process(receive, msg.Message);
            }
            else if (message is WriteMessagesFailure || message is WriteMessagesSuccess)
            {
                if (Actor.ProcessorBatch.Count == 0)
                {
                    _batching = false;
                }
                else
                {
                    JournalBatch();
                }
            }
            else if (message is IPersistentRepresentation)
            {
                AddBatch(message as IPersistentRepresentation);
                if (!_batching || IsMaxSizeReached)
                {
                    JournalBatch();
                }
            }
            else if (message is NonPersistentMessage)
            {
                AddBatch((NonPersistentMessage)message);
                if (!_batching || IsMaxSizeReached)
                {
                    JournalBatch();
                }
            }
            else
            {
                if (Actor.ProcessorBatch.Count == 0)
                {
                    _batching = false;
                }
                else
                {
                    JournalBatch();
                }

                Actor.Journal.Forward(new LoopMessage(message, ActorCell.GetCurrentSelfOrNoSender(), /* ProcessorImpl.instanceId */ 0));
            }
        }

        private void AddBatch(IResequencable resequencable)
        {
            if (resequencable is IPersistentRepresentation)
            {
                var p = resequencable as IPersistentRepresentation;
                // resequencable = p.update(persistenceId = persistenceId, sequenceNr = nextSequenceNr(), sender = sender())
            }

            Actor.ProcessorBatch.Add(resequencable);
        }

        private void JournalBatch()
        {
            FlushJournalBatch();
            _batching = false;
        }

        private void FlushJournalBatch()
        {
            Actor.Journal.Tell(new WriteMessages(Actor.ProcessorBatch, ActorCell.GetCurrentSelfOrNoSender(), /* ProcessorImpl.instanceId */ 0));
            Actor.ProcessorBatch = new List<IResequencable>();
        }

        public override string ToString()
        {
            return "processing";
        }
    }

    public struct PersistenceFailure
    {
        public PersistenceFailure(object payload, long sequenceNr, Exception cause) : this()
        {
            Cause = cause;
            SequenceNr = sequenceNr;
            Payload = payload;
        }

        public object Payload { get; private set; }
        public long SequenceNr { get; private set; }
        public Exception Cause { get; private set; }
    }
}