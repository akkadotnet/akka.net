using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;
using Akka.Streams.Actors;

namespace Akka.Streams.Implementation
{
    public class AcknowledgeSubscriber : Actors.ActorSubscriber
    {
        internal sealed class AnonymousRequestStrategy : IRequestStrategy
        {
            private readonly Func<int, int> _requestDemand;

            public AnonymousRequestStrategy(Func<int, int> requestDemand)
            {
                _requestDemand = requestDemand;
            }

            public int RequestDemand(int remainingRequested)
            {
                return _requestDemand(remainingRequested);
            }
        }

        public static Props Props(int highWatermark)
        {
            return Actor.Props.Create(() => new AcknowledgeSubscriber(highWatermark));
        }

        protected readonly int MaxBuffer;
        protected readonly List<object> Buffer = new List<object>(0);
        protected IActorRef Requester = null;

        private ILoggingAdapter _log;

        public AcknowledgeSubscriber(int maxBuffer)
        {
            MaxBuffer = maxBuffer;
            _requestStrategy = new AnonymousRequestStrategy(remainingRequested => MaxBuffer - Buffer.Count - remainingRequested);
        }

        protected ILoggingAdapter Log { get { return _log ?? (_log = Context.GetLogger()); } }

        private readonly IRequestStrategy _requestStrategy;
        public override IRequestStrategy RequestStrategy
        {
            get { return _requestStrategy; }
        }

        protected override bool Receive(object message)
        {
            if (message is Request)
            {
                if (Requester == null)
                {
                    Requester = Sender;
                    TrySendElementDownStream();
                }else
                    Sender.Tell(new Status.Failure(new IllegalStateException("You have to wait for first future to be resolved to send another request")));
            }
            else if (message is OnNext)
            {
                if (MaxBuffer != 0)
                {
                    Buffer.Add(message);
                    TrySendElementDownStream();
                }
                else 
                {
                    if (Requester != null)
                    {
                        Requester.Tell(message);
                        Requester = null;
                    }
                    else Log.Debug("Dropping element because there is no downstream demand: [{0}]", message);
                }
            }
            else if (message is OnError)
            {
                var err = (OnError) message;
                TrySendDownstream(new Status.Failure(err.Cause));
                Context.Stop(Self);

            }
            else if (message is OnComplete)
            {
                if (Buffer.Count == 0)
                {
                    TrySendDownstream(new Status.Success(null));
                    Context.Stop(Self);
                }
            }
            else return false;
            return true;
        }

        protected void TrySendElementDownStream()
        {
            if (Requester != null)
            {
                if (Buffer.Count > 0)
                {
                    Requester.Tell(Buffer[0]);
                    Requester = null;
                    Buffer.RemoveAt(0);
                }
                else if (IsCanceled)
                {
                    Requester.Tell(null);
                    Context.Stop(Self);
                }
            }
        } 

        private void TrySendDownstream(object element)
        {
            if (Requester != null) Requester.Tell(element);
        }
    }
}