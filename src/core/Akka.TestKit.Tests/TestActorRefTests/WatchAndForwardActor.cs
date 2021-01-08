//-----------------------------------------------------------------------
// <copyright file="WatchAndForwardActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class WatchAndForwardActor : ActorBase
    {
        private readonly IActorRef _forwardToActor;

        public WatchAndForwardActor(IActorRef watchedActor, IActorRef forwardToActor)
        {
            _forwardToActor = forwardToActor;
            Context.Watch(watchedActor);
        }

        protected override bool Receive(object message)
        {
            var terminated = message as Terminated;
            if(terminated != null)
                _forwardToActor.Tell(new WrappedTerminated(terminated), Sender);
            else
                _forwardToActor.Tell(message, Sender);
            return true;
        }
    }
}

