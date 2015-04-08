using Akka.Actor;
using Akka.TestKit;
using Akka.Actor.Dsl;
using Xunit;

namespace Akka.Testkit.Tests
{
    public class NoImplicitSenderSpec : AkkaSpec, INoImplicitSender
    {
        [Fact]
        public void When_Not_ImplicitSender_then_testActor_is_not_sender()
        {
            var echoActor = Sys.ActorOf(c => c.ReceiveAny((m, ctx) => TestActor.Tell(ctx.Sender)));
            echoActor.Tell("message");
            ExpectMsg<IActorRef>(actorRef => actorRef == ActorRefs.NoSender);
        }

    }

    public class ImplicitSenderSpec : AkkaSpec
    {
        [Fact]
        public void ImplicitSender_should_have_testActor_as_sender()
        {
            var echoActor = Sys.ActorOf(c => c.ReceiveAny((m, ctx) => TestActor.Tell(ctx.Sender)));
            echoActor.Tell("message");
            ExpectMsg<IActorRef>(actorRef => actorRef == TestActor);

            //Test that it works after we know that context has been changed
            echoActor.Tell("message");
            ExpectMsg<IActorRef>(actorRef => actorRef == TestActor);

        }


        [Fact]
        public void ImplicitSender_should_not_change_when_creating_Testprobes()
        {
            //Verifies that bug #459 has been fixed
            var testProbe = CreateTestProbe();
            TestActor.Tell("message");
            ReceiveOne();
            LastSender.ShouldBe(TestActor);
        }

        [Fact]
        public void ImplicitSender_should_not_change_when_creating_TestActors()
        {
            var testActor2 = CreateTestActor("test2");
            TestActor.Tell("message");
            ReceiveOne();
            LastSender.ShouldBe(TestActor);
        }
    }

}
