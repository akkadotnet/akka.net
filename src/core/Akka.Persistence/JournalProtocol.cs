using System;
using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Persistence
{
    internal struct DeleteMessages
    {
        public DeleteMessages(IEnumerable<IPersistentEnvelope> messageIds, bool isPermanent, ActorRef requestor)
            : this()
        {
            MessageIds = messageIds;
            IsPermanent = isPermanent;
            Requestor = requestor;
        }

        public IEnumerable<IPersistentEnvelope> MessageIds { get; private set; }
        public bool IsPermanent { get; private set; }
        public ActorRef Requestor { get; private set; }
    }

    internal struct DeleteMessagesSuccess
    {
        public DeleteMessagesSuccess(IEnumerable<IPersistentEnvelope> messageIds)
            : this()
        {
            MessageIds = messageIds;
        }

        public IEnumerable<IPersistentEnvelope> MessageIds { get; private set; }
    }

    /// <summary>
    /// Reply message to failed <see cref="DeleteMessages"/> request.
    /// </summary>
    internal struct DeleteMessagesFailure
    {
        public DeleteMessagesFailure(Exception cause) : this()
        {
            Cause = cause;
        }

        public Exception Cause { get; private set; }
    }

    /// <summary>
    /// Request to delete all persistent messages with sequence numbers up to `toSequenceNr` (inclusive).  
    /// </summary>
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

        /// <summary>
        /// If false, the persistent messages are marked as deleted in the journal, 
        /// otherwise they are permanently deleted from the journal.
        /// </summary>
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
        public WriteMessages(IEnumerable<IPersistentEnvelope> messages, ActorRef persistentActor, int actorInstanceId) : this()
        {
            Messages = messages;
            PersistentActor = persistentActor;
            ActorInstanceId = actorInstanceId;
        }

        public IEnumerable<IPersistentEnvelope> Messages { get; private set; }
        public ActorRef PersistentActor { get; private set; }
        public int ActorInstanceId { get; private set; }
    }

    /// <summary>
    /// Reply message to a successful <see cref="WriteMessages"/> request. This reply is sent 
    /// to the requestor before all subsequent <see cref="WriteMessageSuccess"/> replies.
    /// </summary>
    [Serializable]
    internal class WriteMessagesSuccessull
    {
        public static readonly WriteMessagesSuccessull Instance = new WriteMessagesSuccessull();
        private WriteMessagesSuccessull() { }
    }

    /// <summary>
    /// Reply message to a failed <see cref="WriteMessages"/> request. This reply is sent 
    /// to the requestor before all subsequent <see cref="WriteMessageFailure"/> replies.
    /// </summary>
    internal struct WriteMessagesFailed
    {
        public WriteMessagesFailed(Exception cause)
            : this()
        {
            Cause = cause;
        }

        public Exception Cause { get; private set; }
    }

    /// <summary>
    /// Reply message to a successful <see cref="WriteMessages"/> request. For each contained 
    /// <see cref="IPersistentRepresentation"/> message in the request, a separate reply is sent to the requestor.
    /// </summary>
    internal struct WriteMessageSuccess
    {
        public WriteMessageSuccess(IPersistentRepresentation persistent, int actorInstanceId) : this()
        {
            Persistent = persistent;
            ActorInstanceId = actorInstanceId;
        }

        /// <summary>
        /// Successfully writen message.
        /// </summary>
        public IPersistentRepresentation Persistent { get; private set; }
        public int ActorInstanceId { get; private set; }
    }

    /// <summary>
    /// Reply message to a failed <see cref="WriteMessages"/> request. For each contained 
    /// <see cref="IPersistentRepresentation"/> message in the request, a separate reply is sent to the requestor.
    /// </summary>
    internal struct WriteMessageFailure
    {
        public WriteMessageFailure(IPersistentRepresentation persistent, Exception cause, int actorInstanceId) : this()
        {
            Persistent = persistent;
            Cause = cause;
            ActorInstanceId = actorInstanceId;
        }

        /// <summary>
        /// Message failed to be written.
        /// </summary>
        public IPersistentRepresentation Persistent { get; private set; }

        /// <summary>
        /// Failure cause.
        /// </summary>
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

    /// <summary>
    /// Reply message to a <see cref="WriteMessages"/> with a non-persistent message.
    /// </summary>
    internal struct LoopMessageSuccess
    {
        public LoopMessageSuccess(object message, int actorInstanceId)
            : this()
        {
            Message = message;
            ActorInstanceId = actorInstanceId;
        }

        /// <summary>
        /// A looped message.
        /// </summary>
        public object Message { get; private set; }
        public int ActorInstanceId { get; private set; }
    }

    /// <summary>
    /// Request to replay messages to the <see cref="PersistentActor"/>.
    /// </summary>
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

        /// <summary>
        /// Inclusive lower sequence number bound where a replay should start.
        /// </summary>
        public long FromSequenceNr { get; private set; }

        /// <summary>
        /// Inclusive upper sequence number bound where a replay should end.
        /// </summary>
        public long ToSequenceNr { get; private set; }

        /// <summary>
        /// Maximum number of messages to be replayed.
        /// </summary>
        public long Max { get; private set; }

        /// <summary>
        /// Requesting persistent actor identifier.
        /// </summary>
        public string PersistenceId { get; private set; }

        /// <summary>
        /// Requesting persistent actor.
        /// </summary>
        public ActorRef PersistentActor { get; private set; }

        /// <summary>
        /// If true, message marked as deleted shall be replayed.
        /// </summary>
        public bool ReplayDeleted { get; private set; }
    }

    /// <summary>
    /// Reply message to a <see cref="ReplayMessages"/> request. A separate reply is sent to the requestor for each replayed message.
    /// </summary>
    internal struct ReplayedMessage
    {
        public ReplayedMessage(IPersistentRepresentation persistent) : this()
        {
            Persistent = persistent;
        }

        public IPersistentRepresentation Persistent { get; private set; }
    }

    /// <summary>
    /// Reply message to a successful <see cref="ReplayMessages"/> request. This reply is sent 
    /// to the requestor after all <see cref="ReplayedMessage"/> have been sent (if any).
    /// </summary>
    internal class ReplayMessagesSuccess
    {
        public static readonly ReplayMessagesSuccess Instance = new ReplayMessagesSuccess();
        private ReplayMessagesSuccess() { }
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