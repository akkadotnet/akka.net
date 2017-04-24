using Akka.Actor;
using Akka.Event;
using Xunit;

namespace Akka.Tests.Actor
{
    public partial class ReceiveActorTests
    {
        [Fact]
        public void Given_typed_actor_with_no_receive_specified_When_receiving_message_Then_it_should_be_unhandled()
        {
            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<NoReceiveTypedActor>("no-receive-specified");
            system.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

            //When
            actor.Tell("Something");

            //Then
            ExpectMsg<UnhandledMessage>(m => ((string) m.Message) == "Something" && m.Recipient == actor);
            system.EventStream.Unsubscribe(TestActor, typeof(UnhandledMessage));
        }

        [Fact]
        public void
            Given_typed_actor_with_two_implicit_handle_When_sending_different_messages_Then_correct_handler_should_be_invoked()
        {
            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<ReceiveStringAndIntImplicitTypedActor>("typedImplicitTwoHandle");

            //When
            actor.Tell(4711, TestActor);
            actor.Tell("hello", TestActor);

            //Then
            ExpectMsg("int:4711");
            ExpectMsg("string:hello");
        }

        [Fact]
        public void
            Given_typed_actor_with_two_explicit_handle_When_sending_different_messages_Then_correct_handler_should_be_invoked()
        {
            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<ReceiveStringAndIntExplicitTypedActor>("typedExplicitTwoHandle");

            //When
            actor.Tell(4711, TestActor);
            actor.Tell("hello", TestActor);

            //Then
            ExpectMsg("int:4711");
            ExpectMsg("string:hello");
        }

        private class NoReceiveTypedActor : TypedActor
        {
        }

        private class ReceiveStringAndIntImplicitTypedActor : TypedActor, IHandle<string>, IHandle<int>
        {
            public void Handle(string message)
            {
                Sender.Tell("string:" + message);
            }

            public void Handle(int message)
            {
                Sender.Tell("int:" + message);
            }
        }

        private class ReceiveStringAndIntExplicitTypedActor : TypedActor, IHandle<string>, IHandle<int>
        {
            void IHandle<string>.Handle(string message)
            {
                Sender.Tell("string:" + message);
            }

            void IHandle<int>.Handle(int message)
            {
                Sender.Tell("int:" + message);
            }
        }
    }
}