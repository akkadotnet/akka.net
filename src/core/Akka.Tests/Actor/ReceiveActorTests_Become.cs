using Akka.Actor;
using Akka.Event;
using Xunit;

namespace Akka.Tests.Actor
{
    public partial class ReceiveActorTests
    {
        [Fact]
        public void Given_actor_When_it_calls_Become_Then_it_switches_handler()
        {
            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<BecomeActor>("become");
            system.EventStream.Subscribe(testActor, typeof(UnhandledMessage));

            //When
            actor.Tell("BECOME", testActor);    //Switch to state2   
            actor.Tell("hello", testActor);
            actor.Tell(4711, testActor);
            //Then
            expectMsg("string2:hello", _defaultTimeout);
            expectMsg<UnhandledMessage>(m => ((int)m.Message) == 4711 && m.Recipient == actor, _defaultTimeout);

            //When
            actor.Tell("BECOME", testActor);    //Switch to state3
            actor.Tell("hello", testActor);
            actor.Tell(4711, testActor);
            //Then
            expectMsg("string3:hello", _defaultTimeout);
            expectMsg<UnhandledMessage>(m => ((int)m.Message) == 4711 && m.Recipient == actor, _defaultTimeout);
        }

        [Fact]
        public void Given_actor_that_has_called_Become_When_it_calls_Unbecome_Then_it_switches_back_handler()
        {
            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<BecomeActor>("become");
            actor.Tell("BECOME", testActor);    //Switch to state2
            actor.Tell("BECOME", testActor);    //Switch to state3

            //When
            actor.Tell("UNBECOME", testActor);  //Switch back to state2
            actor.Tell("hello", testActor);

            //Then
            expectMsg("string2:hello", _defaultTimeout);
        }

        [Fact]
        public void Given_actor_that_has_called_Become_at_construction_time_When_it_calls_Unbecome_Then_it_switches_back_handler()
        {
            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<BecomeDirectlyInConstructorActor>("become");

            //When
            actor.Tell("hello", testActor);
            //Then
            expectMsg("string3:hello", _defaultTimeout);

            //When
            actor.Tell("UNBECOME", testActor);  //Switch back to state2
            actor.Tell("hello", testActor);
            //Then
            expectMsg("string2:hello", _defaultTimeout);

            //When
            actor.Tell("UNBECOME", testActor);  //Switch back to state1
            actor.Tell("hello", testActor);
            //Then
            expectMsg("string1:hello", _defaultTimeout);

            //When
            actor.Tell("UNBECOME", testActor);  //should still be in state1
            actor.Tell("hello", testActor);
            //Then
            expectMsg("string1:hello", _defaultTimeout);
        }

        private class BecomeActor : ReceiveActor
        {
            public BecomeActor()
            {
                Receive<string>(s => s == "UNBECOME", __ => Unbecome());
                Receive<string>(s => s == "BECOME", _ => Become(State2, discardOld: false));
                Receive<string>(s => Sender.Tell("string1:" + s, Self));
                Receive<int>(i => Sender.Tell("int1:" + i, Self));
            }

            private void State2()
            {
                Receive<string>(s => s == "UNBECOME", __ => Unbecome());
                Receive<string>(s => s == "BECOME", _ => Become(State3, discardOld: false));
                Receive<string>(s => Sender.Tell("string2:" + s, Self));
            }

            private void State3()
            {
                Receive<string>(s => s == "UNBECOME", __ => Unbecome());
                Receive<string>(s => Sender.Tell("string3:" + s, Self));
            }
        }

        private class BecomeDirectlyInConstructorActor : ReceiveActor
        {
            public BecomeDirectlyInConstructorActor()
            {
                Receive<string>(s => s == "UNBECOME", __ => Unbecome());
                Receive<string>(s => s == "BECOME", _ => Become(State2, discardOld: false));
                Receive<string>(s => Sender.Tell("string1:" + s, Self));
                Receive<int>(i => Sender.Tell("int1:" + i, Self));
                Become(State2, discardOld: false);
                Become(State3, discardOld: false);
            }

            private void State2()
            {
                Receive<string>(s => s == "UNBECOME", __ => Unbecome());
                Receive<string>(s => s == "BECOME", _ => Become(State3, discardOld: false));
                Receive<string>(s => Sender.Tell("string2:" + s, Self));
            }

            private void State3()
            {
                Receive<string>(s => s == "UNBECOME", __ => Unbecome());
                Receive<string>(s => Sender.Tell("string3:" + s, Self));
            }
        }

    }
}