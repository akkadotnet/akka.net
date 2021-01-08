//-----------------------------------------------------------------------
// <copyright file="ActorRefSinkActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using Akka.Streams.Actors;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// TBD
    /// </summary>
    public class ActorRefSinkActor : ActorSubscriber
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ref">TBD</param>
        /// <param name="highWatermark">TBD</param>
        /// <param name="onCompleteMessage">TBD</param>
        /// <returns>TBD</returns>
        public static Props Props(IActorRef @ref, int highWatermark, object onCompleteMessage)
            => Actor.Props.Create(() => new ActorRefSinkActor(@ref, highWatermark, onCompleteMessage));

        private ILoggingAdapter _log;

        /// <summary>
        /// TBD
        /// </summary>
        protected readonly int HighWatermark;
        /// <summary>
        /// TBD
        /// </summary>
        protected readonly IActorRef Ref;
        /// <summary>
        /// TBD
        /// </summary>
        protected readonly object OnCompleteMessage;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ref">TBD</param>
        /// <param name="highWatermark">TBD</param>
        /// <param name="onCompleteMessage">TBD</param>
        public ActorRefSinkActor(IActorRef @ref, int highWatermark, object onCompleteMessage)
        {
            Ref = @ref;
            HighWatermark = highWatermark;
            OnCompleteMessage = onCompleteMessage;
            RequestStrategy = new WatermarkRequestStrategy(highWatermark);

            Context.Watch(Ref);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected ILoggingAdapter Log => _log ?? (_log = Context.GetLogger());

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
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

        /// <summary>
        /// TBD
        /// </summary>
        public override IRequestStrategy RequestStrategy { get; }
    }
}
