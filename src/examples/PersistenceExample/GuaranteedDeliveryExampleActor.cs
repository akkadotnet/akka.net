using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Akka.Persistence;
using Akka.Actor;

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
        public Snap(GuaranteedDeliverySnapshot snapshot)
        {
            this.Snapshot = snapshot;
        }

        public GuaranteedDeliverySnapshot Snapshot { get; private set; }
    }

    public class DeliveryActor : UntypedActor
    {
        bool Confirming = true;

        protected override void OnReceive(object message)
        {
            if (message == "start")
            {
                Confirming = true;
            }
            if (message == "stop")
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
    /// GuaranteedDelivery will repeat sending messages, unless confirmed by deliveryId
    /// 
    /// By default, in-memory Journal is used, so this won't survive system restarts. 
    /// </summary>
    public class GuaranteedDeliveryExampleActor : GuaranteedDeliveryActor
    {
        public ActorPath DeliveryPath { get; private set; }

        public GuaranteedDeliveryExampleActor(ActorPath deliveryPath)
        {
            this.DeliveryPath = deliveryPath;
        }

        public override string PersistenceId
        {
            get { return "guaranteed-1"; }
        }

        protected override bool ReceiveRecover(object message)
        {
            if (message is Message)
            {
                Console.WriteLine("recovered {0}", ((Message)message).Data);
                HandleMessages(message);                
            }
            else if (message is Confirmation)
            {
                Console.WriteLine("recovered confirmation {0}", ((Confirmation)message).DeliveryId);
                HandleMessages(message);
            }
            else
                return false;
            return true;
        }

        protected override bool ReceiveCommand(object message)
        {
            if (message == "boom")
                throw new Exception("Controlled devastation");
            else 
                return HandleMessages(message);
        }

        public bool HandleMessages(Object message)
        {
            if (message is Message)
            {
                this.Persist(message as Message, m =>
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
                this.Persist(message as Confirmation, m => ConfirmDelivery(m.DeliveryId));                
            }
            else return false;

            return true;            
        }


    }
}
