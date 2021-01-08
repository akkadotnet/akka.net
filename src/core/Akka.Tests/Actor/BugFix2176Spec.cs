//-----------------------------------------------------------------------
// <copyright file="BugFix2176Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    public class BugFix2176Spec : AkkaSpec
    {
        private class Actor1 : ReceiveActor
        {
            private readonly IActorRef _testActor;

            public Actor1(IActorRef testActor)
            {
                _testActor = testActor;
                Receive<string>(m => { _testActor.Tell(m); });

                // if RunTask is omitted, everything works fine
                // otherwise actor2 never receives messages from its child actor
                RunTask(() => { Context.ActorOf(Props.Create<Actor2>(), "actor-2"); });
            }
        }

        private class Actor1NonAsync : ReceiveActor
        {
            private readonly IActorRef _testActor;

            public Actor1NonAsync(IActorRef testActor)
            {
                _testActor = testActor;
                Receive<string>(m => { _testActor.Tell(m); });

                Context.ActorOf(Props.Create<Actor2>(), "actor-2");
            }
        }

        private class Actor2 : ReceiveActor
        {
            public Actor2()
            {
                Receive<string>(m => { Context.Parent.Tell(m); });
            }

            protected override void PreStart()
            {
                Self.Tell("started");
            }
        }

        [Fact]
        public void Fix2176_Constructor_Should_create_valid_child_actor()
        {
            var actor = Sys.ActorOf(Props.Create(() => new Actor1NonAsync(TestActor)), "actor1");
            ExpectMsg("started");
        }

        [Fact]
        public void Fix2176_RunTask_Should_create_valid_child_actor()
        {
            var actor = Sys.ActorOf(Props.Create(() => new Actor1(TestActor)), "actor1");
            ExpectMsg("started");
        }
    }
}
