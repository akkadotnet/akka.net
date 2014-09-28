﻿using System;
using Akka.Actor;
using Akka.TestKit;
using Akka.Actor.Dsl;
using Xunit;

namespace Akka.Testkit.Tests
{
    public class NoImplicitSenderSpec : AkkaSpec, NoImplicitSender
    {
        [Fact]
        public void When_Not_ImplicitSender_then_testActor_is_not_sender()
        {
            var echoActor = Sys.ActorOf(c => c.ReceiveAny((m, ctx) => TestActor.Tell(ctx.Sender)));
            echoActor.Tell("message");
            ExpectMsg<ActorRef>(actorRef => actorRef == DeadLetterActorRef.NoSender);
        }
    }

    public class ImplicitSenderSpec : AkkaSpec
    {
        [Fact]
        public void ImplicitSender_should_have_testActor_as_sender()
        {
            var echoActor = Sys.ActorOf(c => c.ReceiveAny((m, ctx) => TestActor.Tell(ctx.Sender)));
            echoActor.Tell("message");
            ExpectMsg<ActorRef>(actorRef => actorRef == TestActor);

            //Test that it works after we know that context has been changed
            echoActor.Tell("message");
            ExpectMsg<ActorRef>(actorRef => actorRef == TestActor);

        }


    }

}
