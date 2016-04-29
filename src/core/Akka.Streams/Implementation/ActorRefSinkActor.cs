//-----------------------------------------------------------------------
// <copyright file="ActorRefSinkActor.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using Akka.Streams.Actors;

namespace Akka.Streams.Implementation
{
    public class ActorRefSinkActor : ActorSubscriber
    {
        public static Props Props(IActorRef @ref, int highWatermark, object onCompleteMessage)
            => Actor.Props.Create(() => new ActorRefSinkActor(@ref, highWatermark, onCompleteMessage));

        private ILoggingAdapter _log;

        protected readonly int HighWatermark;
        protected readonly IActorRef Ref;
        protected readonly object OnCompleteMessage;

        public ActorRefSinkActor(IActorRef @ref, int highWatermark, object onCompleteMessage)
        {
            Ref = @ref;
            HighWatermark = highWatermark;
            OnCompleteMessage = onCompleteMessage;
            RequestStrategy = new WatermarkRequestStrategy(highWatermark);

            Context.Watch(Ref);
        }

        protected ILoggingAdapter Log => _log ?? (_log = Context.GetLogger());

        protected override bool Receive(object message)
        {
            Terminated terminated;
            if (message is OnNext)
            {
                var onNext = (OnNext) message;
                Ref.Tell(onNext.Element);
            }
            else if (message is OnError)
            {
                var onError = (OnError) message;
                Ref.Tell(new Status.Failure(onError.Cause));
                Context.Stop(Self);
            }
            else if (message is OnComplete)
            {
                Ref.Tell(OnCompleteMessage);
                Context.Stop(Self);
            }
            else if ((terminated = message as Terminated) != null && terminated.ActorRef.Equals(Ref))
                Context.Stop(Self); // will cancel upstream
            else
                return false;

            return true;
        }

        public override IRequestStrategy RequestStrategy { get; }
    }
}