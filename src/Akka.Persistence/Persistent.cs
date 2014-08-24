using System.Collections.Generic;
using Akka.Actor;
using Akka.Persistence.Serialization;

namespace Akka.Persistence
{
    public interface IResequencable
    {
        object Payload { get; }
        ActorRef Sender { get; }
    }

    internal struct NonPersistentMessage : IResequencable
    {
        public NonPersistentMessage(object payload, ActorRef sender) : this()
        {
            Payload = payload;
            Sender = sender;
        }

        public object Payload { get; private set; }
        public ActorRef Sender { get; private set; }
    }

    public abstract class PersistentBase : IResequencable
    {
        protected PersistentBase(object payload, ActorRef sender = null)
        {
            Payload = payload;
            Sender = sender;
        }

        public object Payload { get; protected set; }
        public ActorRef Sender { get; protected set; }

        public abstract PersistentBase WithPayload(object payload);
    }

    public interface IPersistentRepresentation : IMessage, IResequencable
    {
        bool IsDeleted { get; }

        IPersistentRepresentation Update(long sequenceNr, bool isDeleted, ActorRef sender);
    }

    public sealed class Persistent : PersistentBase, IPersistentRepresentation
    {
        public Persistent(object payload, ActorRef sender = null) 
            : base(payload, sender)
        {
        }

        public Persistent(object payload, long sequenceNr, bool isDeleted, ActorRef sender, IEnumerable<string> confirms) 
            : base(null, null)
        {
            
        }

        public bool IsDeleted { get; private set; }
        public int Redeliverables { get; set; }
        public bool Confirmable { get; set; }
        public ActorRef ConfirmTarget { get; set; }

        public override PersistentBase WithPayload(object payload)
        {
            throw new System.NotImplementedException();
        }
        public IPersistentRepresentation Update(long sequenceNr, bool isDeleted, ActorRef sender)
        {
            throw new System.NotImplementedException();
        }
    }

    public interface IPersistentId
    {
        string PersistenceId { get; }
        long SequenceNr { get; }
    }

    public interface IPersistentConfirmation
    {
        string PersistenceId { get; }
        long SequenceNr { get; }
    }
}