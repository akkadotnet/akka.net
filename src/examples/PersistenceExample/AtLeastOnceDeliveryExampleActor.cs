//-----------------------------------------------------------------------
// <copyright file="AtLeastOnceDeliveryExampleActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
            Data = data;
        }

        public string Data { get; }
    }

    public class Confirmable
    {
        public Confirmable(long deliveryId, string data)
        {
            DeliveryId = deliveryId;
            Data = data;
        }

        public long DeliveryId { get; }

        public string Data { get; }
    }
    
    public class Confirmation
    {
        public Confirmation(long deliveryId)
        {
            DeliveryId = deliveryId;
        }

        public long DeliveryId { get; }
    }
    
    [Serializable]
    public class Snap
    {
        public Snap(AtLeastOnceDeliverySnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public AtLeastOnceDeliverySnapshot Snapshot { get; }
    }

    public class DeliveryActor : UntypedActor
    {
        private bool _confirming = true;

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case string str when str == "start":
                    _confirming = true;
                    break;
                
                case string str when str == "stop":
                    _confirming = false;
                    break;
                
                case Confirmable msg:
                    if (_confirming)
                    {
                        Console.WriteLine("Confirming delivery of message id: {0} and data: {1}", msg.DeliveryId, msg.Data);
                        Context.Sender.Tell(new Confirmation(msg.DeliveryId));
                    }
                    else
                    {
                        Console.WriteLine("Ignoring message id: {0} and data: {1}", msg.DeliveryId, msg.Data);
                    }
                    break;
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
        private ActorPath DeliveryPath { get; }

        public AtLeastOnceDeliveryExampleActor(ActorPath deliveryPath)
        {
            DeliveryPath = deliveryPath;
        }

        public override string PersistenceId
        {
            get { return "at-least-once-1"; }
        }

        protected override bool ReceiveRecover(object message)
        {
            switch (message)
            {
                case Message msg:
                    var messageData =  msg.Data;
                    Console.WriteLine("recovered {0}",messageData);
                    Deliver(DeliveryPath,
                        id =>
                        {
                            Console.WriteLine("recovered delivery task: {0}, with deliveryId: {1}", messageData, id);
                            return new Confirmable(id, messageData);
                        });
                    return true;
                
                case Confirmation confirm:
                    var deliveryId = confirm.DeliveryId;
                    Console.WriteLine("recovered confirmation of {0}", deliveryId);
                    ConfirmDelivery(deliveryId);
                    return true;
                
                default:
                    return false;
            }
        }

        protected override bool ReceiveCommand(object message)
        {
            switch (message)
            {
                case string str when str == "boom":
                    throw new Exception("Controlled devastation");
                
                case Message msg:
                    Persist(msg, m =>
                        Deliver(DeliveryPath,
                            id => {
                                Console.WriteLine("sending: {0}, with deliveryId: {1}", m.Data, id);
                                return new Confirmable(id, m.Data);
                            })
                    );
                    return true;
                    
                case Confirmation confirm:
                    Persist(confirm, m => ConfirmDelivery(m.DeliveryId));
                    return true;
                
                default:
                    return false;
            }
        }
    }
}
