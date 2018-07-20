﻿using Akka.Actor;
using Akka.Persistence;

namespace DocsExamples.Persistence.AtLeastOnceDelivery
{
    public class ExampleAtLeastOnceDeliveryReceiveActor : AtLeastOnceDeliveryReceiveActor
    {
        private readonly IActorRef _destionationActor = Context.ActorOf<ExampleDestinationAtLeastOnceDeliveryReceiveActor>();

        public ExampleAtLeastOnceDeliveryReceiveActor()
        {
            Recover<MsgSent>(msgSent => Handler(msgSent));
            Recover<MsgConfirmed>(msgConfirmed => Handler(msgConfirmed));

            Command<string>(str =>
            {
                Persist(new MsgSent(str), Handler);
            });

            Command<Confirm>(confirm =>
            {
                Persist(new MsgConfirmed(confirm.DeliveryId), Handler);
            });
        }

        private void Handler(MsgSent msgSent)
        {
            Deliver(_destionationActor.Path, l => new Msg(l, msgSent.Message));
        }

        private void Handler(MsgConfirmed msgConfirmed)
        {
            ConfirmDelivery(msgConfirmed.DeliveryId);
        }

        public override string PersistenceId { get; } = "persistence-id";
    }

    public class ExampleDestinationAtLeastOnceDeliveryReceiveActor : ReceiveActor
    {
        public ExampleDestinationAtLeastOnceDeliveryReceiveActor()
        {
            Receive<Msg>(msg =>
            {
                Sender.Tell(new Confirm(msg.DeliveryId), Self);
            });
        }
    }
}
