//-----------------------------------------------------------------------
// <copyright file="NoImplicitSenderSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Xunit;

namespace Akka.TestKit.Tests
{
    public class NoImplicitSenderSpec : AkkaSpec, INoImplicitSender
    {
        [Fact(Skip = "Type assertion on null message causes NullReferenceException")]
        public async Task When_Not_ImplicitSender_then_testActor_is_not_sender()
        {
            var echoActor = Sys.ActorOf(c => c.ReceiveAny((m, ctx) => TestActor.Tell(ctx.Sender)));
            echoActor.Tell("message");
            await ExpectMsgAsync<IActorRef>(actorRef => Equals(actorRef, ActorRefs.NoSender));
        }

    }

    public class ImplicitSenderSpec : AkkaSpec
    {
        [Fact]
        public async Task ImplicitSender_should_have_testActor_as_sender()
        {
            var echoActor = Sys.ActorOf(c => c.ReceiveAny((m, ctx) => TestActor.Tell(ctx.Sender)));
            echoActor.Tell("message");
            await ExpectMsgAsync<IActorRef>(actorRef => Equals(actorRef, TestActor));

            //Test that it works after we know that context has been changed
            echoActor.Tell("message");
            await ExpectMsgAsync<IActorRef>(actorRef => Equals(actorRef, TestActor));

        }


        [Fact]
        public async Task ImplicitSender_should_not_change_when_creating_Testprobes()
        {
            //Verifies that bug #459 has been fixed
            var testProbe = CreateTestProbe();
            TestActor.Tell("message");
            await ReceiveOneAsync();
            LastSender.ShouldBe(TestActor);
        }

        [Fact]
        public async Task ImplicitSender_should_not_change_when_creating_TestActors()
        {
            var testActor2 = CreateTestActor("test2");
            TestActor.Tell("message");
            await ReceiveOneAsync();
            LastSender.ShouldBe(TestActor);
        }
    }
}

