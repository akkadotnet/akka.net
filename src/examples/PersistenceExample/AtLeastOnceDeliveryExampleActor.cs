//-----------------------------------------------------------------------
// <copyright file="AtLeastOnceDeliveryExampleActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Persistence;

namespace PersistenceExample
{
    public class Message
    {
        public Message(string data)
        {
            this.Data = data;
        }

        public string Data { get; private set; }
    }

    public class Confirmable
    {
        public Confirmable(long deliveryId, string data)
        {
            this.DeliveryId = deliveryId;
            this.Data = data;
        }


        public long DeliveryId { get; private set; }

        public string Data { get; private set; }
    }
    public class Confirmation
    {
        public Confirmation(long deliveryId)
        {
            this.DeliveryId = deliveryId;
        }

        public long DeliveryId { get; private set; }
    }
    [Serializable]
    public class Snap
    {
        public Snap(Akka.Persistence.AtLeastOnceDeliverySnapshot snapshot)
        {
            this.Snapshot = snapshot;
        }

        public Akka.Persistence.AtLeastOnceDeliverySnapshot Snapshot { get; private set; }
    }

    public class DeliveryActor : UntypedActor
    {
        bool Confirming = true;

        protected override void OnReceive(object message)
        {
            if (message as string == "start")
            {
                Confirming = true;
            }
            if (message as string == "stop")
            {
                Confirming = false;
            }
            if (message is Confirmable)
            {
                var msg = message as Confirmable;
                if (Confirming)
                {
                    Console.WriteLine("Confirming delivery of message id: {0} and data: {1}", msg.DeliveryId, msg.Data);
                    Context.Sender.Tell(new Confirmation(msg.DeliveryId));
                }
                else
                {
                    Console.WriteLine("Ignoring message id: {0} and data: {1}", msg.DeliveryId, msg.Data);
                }
            }
        }
    }
    /// <summary>
    /// AtLeastOnceDelivery will repeat sending messages, unless confirmed by deliveryId
    /// 
    /// By default, in-memory Journal is used, so this won't survive system restarts. 
    /// </summary>
    public class AtLeastOnceDeliveryExampleActor : AtLeastOnceDeliveryActor
    {
        public ActorPath DeliveryPath { get; private set; }

        public AtLeastOnceDeliveryExampleActor(ActorPath deliveryPath)
        {
            this.DeliveryPath = deliveryPath;
        }

        public override string PersistenceId
        {
            get { return "at-least-once-1"; }
        }

        protected override bool ReceiveRecover(object message)
        {
            if (message is Message)
            {
                var messageData =  ((Message)message).Data;
                Console.WriteLine("recovered {0}",messageData);
                Deliver(DeliveryPath,
                        id =>
                        {
                            Console.WriteLine("recovered delivery task: {0}, with deliveryId: {1}", messageData, id);
                            return new Confirmable(id, messageData);
                        });
                
            }
            else if (message is Confirmation)
            {
                var deliveryId = ((Confirmation)message).DeliveryId;
                Console.WriteLine("recovered confirmation of {0}", deliveryId);
                ConfirmDelivery(deliveryId);
            }
            else
                return false;
            return true;
        }

        protected override bool ReceiveCommand(object message)
        {
            if (message as string == "boom")
                throw new Exception("Controlled devastation");
            else if (message is Message)
            {
                Persist(message as Message, m =>
                {
                    Deliver(DeliveryPath,
                        id =>
                        {
                            Console.WriteLine("sending: {0}, with deliveryId: {1}", m.Data, id);
                            return new Confirmable(id, m.Data);
                        });
                });
            }
            else if (message is Confirmation)
            {
                Persist(message as Confirmation, m => ConfirmDelivery(m.DeliveryId));
            }
            else return false;
            return true;
        }
    }
}
