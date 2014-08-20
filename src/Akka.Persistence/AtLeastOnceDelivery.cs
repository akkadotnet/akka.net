using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Persistence.Serialization;

namespace Akka.Persistence
{
    public class MaxUnconfirmedMessagesExceededException : AkkaException
    {
        public MaxUnconfirmedMessagesExceededException(string message, Exception cause = null) : base(message, cause)
        {
        }
    }

    public struct AtLeastOnceDeliverySnapshot : IMessage
    {
        public AtLeastOnceDeliverySnapshot(long deliveryId, IEnumerable<UnconfirmedDelivery> unconfirmedDeliveries)
            : this()
        {
            DeliveryId = deliveryId;
            UnconfirmedDeliveries = unconfirmedDeliveries;
        }

        public long DeliveryId { get; private set; }
        public IEnumerable<UnconfirmedDelivery> UnconfirmedDeliveries { get; private set; }
    }

    public struct UnconfirmedWarning
    {
        public UnconfirmedWarning(IEnumerable<UnconfirmedDelivery> unconfirmedDeliveries) : this()
        {
            UnconfirmedDeliveries = unconfirmedDeliveries;
        }

        public IEnumerable<UnconfirmedDelivery> UnconfirmedDeliveries { get; set; }
    }

    public struct UnconfirmedDelivery
    {
        public UnconfirmedDelivery(long deliveryId, ActorPath destination, object message) : this()
        {
            DeliveryId = deliveryId;
            Destination = destination;
            Message = message;
        }

        public long DeliveryId { get; set; }
        public ActorPath Destination { get; set; }
        public object Message { get; set; }
    }

    public interface IAtLeastOnceDelivery
    {
        TimeSpan RedeliverInterval { get; }
        int NumberOfUnconfirmedAttemptsWarning { get; }
        int MaxUnconfirmedMessages { get; }
        int UnconfirmedMessages { get; }
        AtLeastOnceDeliverySnapshot DeliverySnapshot { get; set; }

        void Deliver(ActorPath destination, Func<long, object> idToMessageMapper);

        bool ConfirmDelivery(long deliveryId);
    }

    public class PersistentAtLeastOnceDeliveryBase : PersistentActorBase, IAtLeastOnceDelivery
    {
        public TimeSpan RedeliverInterval { get; private set; }
        public int NumberOfUnconfirmedAttemptsWarning { get; private set; }
        public int MaxUnconfirmedMessages { get; private set; }
        public int UnconfirmedMessages { get; private set; }
        public AtLeastOnceDeliverySnapshot DeliverySnapshot { get; set; }
        public void Deliver(ActorPath destination, Func<long, object> idToMessageMapper)
        {
            throw new NotImplementedException();
        }

        public bool ConfirmDelivery(long deliveryId)
        {
            throw new NotImplementedException();
        }
    }
}