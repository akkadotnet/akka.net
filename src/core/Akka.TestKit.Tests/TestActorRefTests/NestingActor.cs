//-----------------------------------------------------------------------
// <copyright file="NestingActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class NestingActor : ActorBase
    {
        private readonly IActorRef _nested;

        public NestingActor(bool createTestActorRef)
        {
            _nested = createTestActorRef ? Context.System.ActorOf<NestedActor>() : new TestActorRef<NestedActor>(Context.System, Props.Create<NestedActor>(), null, null);
        }

        protected override bool Receive(object message)
        {
            Sender.Tell(_nested, Self);
            return true;
        }

        private class NestedActor : ActorBase
        {
            protected override bool Receive(object message)
            {
                return true;
            }
        }
    }
}

