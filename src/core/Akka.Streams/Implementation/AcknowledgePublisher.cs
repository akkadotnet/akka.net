using System;
using Akka.Actor;
using Akka.Streams.Actors;

namespace Akka.Streams.Implementation
{
    internal class AcknowledgePublisher : ActorRefSourceActor
    {
        #region Messages

        [Serializable]
        public sealed class Ok
        {
            public static readonly Ok Instance = new Ok();
            private Ok() { }
        }

        [Serializable]
        public sealed class Rejected
        {
            public static readonly Rejected Instance = new Rejected();
            private Rejected() { }
        }

        #endregion

        public static Props Props(int bufferSize, OverflowStrategy overflowStrategy)
        {
            return Actor.Props.Create(() => new AcknowledgePublisher(bufferSize, overflowStrategy));
        }

        protected IActorRef BackpressedElement = null;

        public AcknowledgePublisher(int bufferSize, OverflowStrategy overflowStrategy)
            : base(bufferSize, overflowStrategy)
        {
        }

        protected override bool RequestElement(object message)
        {
            if (message is Request)
            {
                // totalDemand is tracked by base
                if (BufferSize != 0)
                    while (TotalDemand > 0L && !Buffer.IsEmpty)
                    {
                        //if buffer is full - sent ack message to sender in case of Backpressure mode
                        if (Buffer.IsFull && BackpressedElement != null)
                        {
                            BackpressedElement.Tell(Ok.Instance);
                            BackpressedElement = null;
                        }

                        OnNext(Buffer.Dequeue());
                    }

                return true;
            }

            return false;
        }

        protected override bool ReceiveElement(object message)
        {
            if (IsActive)
            {
                if (TotalDemand > 0L)
                {
                    OnNext(message);
                    SendAck(true);
                }
                else if (BufferSize == 0)
                {
                    Log.Debug("Dropping element because there is no downstream demand: [{0}]", message);
                    SendAck(false);
                }
                else if (!Buffer.IsFull) EnqueueAndSendAck(message);
                else
                {
                    switch (OverflowStrategy)
                    {
                        case OverflowStrategy.DropHead:
                            Log.Debug("Dropping the head element because buffer is full and overflowStrategy is: [DropHead]");
                            Buffer.DropHead();
                            EnqueueAndSendAck(message);
                            break;
                        case OverflowStrategy.DropTail:
                            Log.Debug("Dropping the tail element because buffer is full and overflowStrategy is: [DropTail]");
                            Buffer.DropTail();
                            EnqueueAndSendAck(message);
                            break;
                        case OverflowStrategy.DropBuffer:
                            Log.Debug("Dropping all the buffered elements because buffer is full and overflowStrategy is: [DropBuffer]");
                            Buffer.Clear();
                            EnqueueAndSendAck(message);
                            break;
                        case OverflowStrategy.DropNew:
                            Log.Debug("Dropping the new element because buffer is full and overflowStrategy is: [DropNew]");
                            SendAck(false);
                            break;
                        case OverflowStrategy.Fail:
                            Log.Error("Failing because buffer is full and overflowStrategy is: [Fail]");
                            OnErrorThenStop(new BufferOverflowException(string.Format("Buffer overflow, max capacity was ({0})", BufferSize)));
                            break;
                        case OverflowStrategy.Backpressure:
                            Log.Debug("Backpressuring because buffer is full and overflowStrategy is: [Backpressure]");
                            SendAck(false); //does not allow to send more than buffer size
                            break;
                    }
                }

                return true;
            }
            else return false;
        }

        protected void EnqueueAndSendAck(object element)
        {
            Buffer.Enqueue(element);
            if (Buffer.IsFull && OverflowStrategy == OverflowStrategy.Backpressure) BackpressedElement = Sender;
            else SendAck(true);
        }

        protected void SendAck(bool isOk)
        {
            var msg = isOk ? (object)Ok.Instance : Rejected.Instance;
            Context.Sender.Tell(msg);
        }
    }
}