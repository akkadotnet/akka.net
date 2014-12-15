using System;
using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Persistence
{
    internal struct DeleteMessages
    {
        public DeleteMessages(IEnumerable<IPersistentId> messageIds, bool isPermanent, ActorRef requestor)
            : this()
        {
            MessageIds = messageIds;
            IsPermanent = isPermanent;
            Requestor = requestor;
        }

        public IEnumerable<IPersistentId> MessageIds { get; private set; }
        public bool IsPermanent { get; private set; }
        public ActorRef Requestor { get; private set; }
    }

    internal struct DeleteMessagesSuccess
    {
        public DeleteMessagesSuccess(IEnumerable<IPersistentId> messageIds) : this()
        {
            MessageIds = messageIds;
        }

        public IEnumerable<IPersistentId> MessageIds { get; private set; }
    }

    internal struct DeleteMessagesFailure
    {
        public DeleteMessagesFailure(Exception cause) : this()
        {
            Cause = cause;
        }

        public Exception Cause { get; private set; }
    }

    internal struct DeleteMessagesTo
    {
        public DeleteMessagesTo(string persistenceId, long toSequenceNr, bool isPermanent) : this()
        {
            PersistenceId = persistenceId;
            ToSequenceNr = toSequenceNr;
            IsPermanent = isPermanent;
        }

        public string PersistenceId { get; private set; }
        public long ToSequenceNr { get; private set; }
        public bool IsPermanent { get; private set; }
    }

    internal struct WriteConfirmations
    {
        public WriteConfirmations(IEnumerable<IPersistentConfirmation> confirmations, ActorRef requestor) : this()
        {
            Confirmations = confirmations;
            Requestor = requestor;
        }

        public IEnumerable<IPersistentConfirmation> Confirmations { get; private set; }
        public ActorRef Requestor { get; private set; }
    }
    internal struct WriteConfirmationsSuccess
    {
        public WriteConfirmationsSuccess(IEnumerable<IPersistentConfirmation> confirmations)
            : this()
        {
            Confirmations = confirmations;
        }

        public IEnumerable<IPersistentConfirmation> Confirmations { get; private set; }
    }

    internal struct WriteConfirmationsFailure
    {
        public WriteConfirmationsFailure(Exception cause)
            : this()
        {
            Cause = cause;
        }

        public Exception Cause { get; private set; }
    }

    internal struct WriteMessages
    {
        public WriteMessages(IEnumerable<IResequencable> messages, ActorRef persistentActor, int actorInstanceId) : this()
        {
            Messages = messages;
            PersistentActor = persistentActor;
            ActorInstanceId = actorInstanceId;
        }

        public IEnumerable<IResequencable> Messages { get; private set; }
        public ActorRef PersistentActor { get; private set; }
        public int ActorInstanceId { get; private set; }
    }

    internal class WriteMessagesSuccess
    {
        public static readonly WriteMessagesSuccess Instance = new WriteMessagesSuccess();
        private WriteMessagesSuccess()
        {
        }
    }

    internal struct WriteMessagesFailure
    {
        public WriteMessagesFailure(Exception cause)
            : this()
        {
            Cause = cause;
        }

        public Exception Cause { get; private set; }
    }

    internal struct WriteMessageSuccess
    {
        public WriteMessageSuccess(IPersistentRepresentation persistent, int actorInstanceId) : this()
        {
            Persistent = persistent;
            ActorInstanceId = actorInstanceId;
        }

        public IPersistentRepresentation Persistent { get; private set; }
        public int ActorInstanceId { get; private set; }
    }

    internal struct WriteMessageFailure
    {
        public WriteMessageFailure(IPersistentRepresentation persistent, Exception cause, int actorInstanceId) : this()
        {
            Persistent = persistent;
            Cause = cause;
            ActorInstanceId = actorInstanceId;
        }

        public IPersistentRepresentation Persistent { get; private set; }
        public Exception Cause { get; private set; }
        public int ActorInstanceId { get; private set; }
    }

    internal struct LoopMessage
    {
        public LoopMessage(object message, ActorRef persistentActor, int actorInstanceId) : this()
        {
            Message = message;
            PersistentActor = persistentActor;
            ActorInstanceId = actorInstanceId;
        }

        public object Message { get; private set; }
        public ActorRef PersistentActor { get; private set; }
        public int ActorInstanceId { get; private set; }
    }
    internal struct LoopMessageSuccess
    {
        public LoopMessageSuccess(object message, int actorInstanceId)
            : this()
        {
            Message = message;
            ActorInstanceId = actorInstanceId;
        }

        public object Message { get; private set; }
        public int ActorInstanceId { get; private set; }
    }

    internal struct ReplayMessages
    {
        public ReplayMessages(long fromSequenceNr, long toSequenceNr, long max, string persistenceId, ActorRef persistentActor, bool replayDeleted = false) 
            : this()
        {
            FromSequenceNr = fromSequenceNr;
            ToSequenceNr = toSequenceNr;
            Max = max;
            PersistenceId = persistenceId;
            PersistentActor = persistentActor;
            ReplayDeleted = replayDeleted;
        }

        public long FromSequenceNr { get; private set; }
        public long ToSequenceNr { get; private set; }
        public long Max { get; private set; }
        public string PersistenceId { get; private set; }
        public ActorRef PersistentActor { get; private set; }
        public bool ReplayDeleted { get; private set; }
    }

    internal struct ReplayedMessage
    {
        public ReplayedMessage(IPersistentRepresentation persistent) : this()
        {
            Persistent = persistent;
        }

        public IPersistentRepresentation Persistent { get; private set; }
    }

    internal class ReplayMessagesSuccess
    {
        public static readonly ReplayMessagesSuccess Instance = new ReplayMessagesSuccess();
        private ReplayMessagesSuccess()
        {
        }
    }

    internal struct ReplayMessagesFailure
    {
        public ReplayMessagesFailure(Exception cause)
            : this()
        {
            Cause = cause;
        }

        public Exception Cause { get; private set; }
    }

    internal struct ReadHighestSequenceNr
    {
        public ReadHighestSequenceNr(long fromSequenceNr, string persistenceId, ActorRef persistentActor) : this()
        {
            FromSequenceNr = fromSequenceNr;
            PersistenceId = persistenceId;
            PersistentActor = persistentActor;
        }

        public long FromSequenceNr { get; private set; }
        public string PersistenceId { get; private set; }
        public ActorRef PersistentActor { get; private set; }
    }

    internal struct ReadHighestSequenceNrSuccess
    {
        public ReadHighestSequenceNrSuccess(long highestSequenceNr) : this()
        {
            HighestSequenceNr = highestSequenceNr;
        }

        public long HighestSequenceNr { get; private set; }
    }

    internal struct ReadHighestSequenceNrFailure
    {
        public ReadHighestSequenceNrFailure(Exception cause)
            : this()
        {
            Cause = cause;
        }

        public Exception Cause { get; private set; }
    }
}