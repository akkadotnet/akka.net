using System;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Actors;

namespace Akka.Streams.Implementation
{
    public class ActorRefSinkActor : Actors.ActorSubscriber
    {
        public static Props Props(IActorRef @ref, int highWatermark, object onCompleteMessage)
        {
            return Actor.Props.Create(() => new ActorRefSinkActor(@ref, highWatermark, onCompleteMessage));
        }

        private ILoggingAdapter _log;

        protected readonly int HighWatermark;
        protected readonly IActorRef Ref;
        protected readonly object OnCompleteMessage;

        public ActorRefSinkActor(IActorRef @ref, int highWatermark, object onCompleteMessage)
        {
            Ref = @ref;
            HighWatermark = highWatermark;
            OnCompleteMessage = onCompleteMessage;
            _requestStrategy = new WatermarkRequestStrategy(highWatermark);

            Context.Watch(Ref);
        }

        protected ILoggingAdapter Log { get { return _log ?? (_log = Context.GetLogger()); } }

        protected override bool Receive(object message)
        {
            Terminated terminated;
            if(message is OnNext) Ref.Tell(message);
            else if (message is OnError)
            {
                var err = (OnError) message;
                Ref.Tell(new Status.Failure(err.Cause));
                Context.Stop(Self);
            }
            else if (message is OnComplete)
            {
                Ref.Tell(OnCompleteMessage);
                Context.Stop(Self);
            }
            else if ((terminated = message as Terminated) != null && terminated.ActorRef.Equals(Ref))
            {
                Context.Stop(Self); // will cancel upstream
            }
            else return false;
            return true;
        }

        private readonly IRequestStrategy _requestStrategy;
        public override IRequestStrategy RequestStrategy
        {
            get { return _requestStrategy; }
        }
    }
}