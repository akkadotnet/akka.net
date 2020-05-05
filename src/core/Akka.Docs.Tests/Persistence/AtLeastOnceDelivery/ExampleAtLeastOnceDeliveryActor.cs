//-----------------------------------------------------------------------
// <copyright file="ExampleAtLeastOnceDeliveryActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Persistence;

namespace DocsExamples.Persistence.AtLeastOnceDelivery
{
    public class ExampleAtLeastOnceDeliveryActor : AtLeastOnceDeliveryActor
    {
        private ActorSelection _destination;

        public ExampleAtLeastOnceDeliveryActor(ActorSelection destination)
        {
            _destination = destination;
        }

        protected override bool ReceiveCommand(object message)
        {
            switch (message)
            {
                case string s:
                    Persist(new MsgSent(s), UpdateState);
                    return true;
                case Confirm confirm:
                    Persist(new MsgConfirmed(confirm.DeliveryId), UpdateState);
                    return true;
                default:
                    return false;
            }
        }

        protected override bool ReceiveRecover(object message)
        {
            switch (message)
            {
                case IEvent evt:
                    UpdateState(evt);
                    return true;
                default:
                    return false;
            }
        }

        private void UpdateState(IEvent evt)
        {
            switch (evt)
            {
                case MsgSent msgSent:
                    Deliver(_destination, deliveryId => new Msg(deliveryId, msgSent.Message));
                    break;
                case MsgConfirmed msgConfirmed:
                    ConfirmDelivery(msgConfirmed.DeliveryId);
                    break;
            }
        }

        public override string PersistenceId { get; } = "persistence-id";
    }

    public class ExampleDestinationAtLeastOnceDeliveryActor : UntypedActor
    {
        protected override void OnReceive(object message)
        {
            if (message is Msg msg)
            {
                Sender.Tell(new Confirm(msg.DeliveryId), Self);
            }
        }
    }
}
