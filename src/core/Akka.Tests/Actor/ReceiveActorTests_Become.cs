//-----------------------------------------------------------------------
// <copyright file="ReceiveActorTests_Become.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
            system.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

            //When
            actor.Tell("BECOME", TestActor);    //Switch to state2   
            actor.Tell("hello", TestActor);
            actor.Tell(4711, TestActor);
            //Then
            ExpectMsg((object) "string2:hello");
            ExpectMsg<UnhandledMessage>( m => ((int)m.Message) == 4711 && m.Recipient == actor);

            //When
            actor.Tell("BECOME", TestActor);    //Switch to state3
            actor.Tell("hello", TestActor);
            actor.Tell(4711, TestActor);
            //Then
            ExpectMsg((object) "string3:hello");
            ExpectMsg<UnhandledMessage>(m => ((int)m.Message) == 4711 && m.Recipient == actor);
        }

        [Fact]
        public void Given_actor_that_has_called_Become_When_it_calls_Unbecome_Then_it_switches_back_handler()
        {
            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<BecomeActor>("become");
            actor.Tell("BECOME", TestActor);    //Switch to state2
            actor.Tell("BECOME", TestActor);    //Switch to state3

            //When
            actor.Tell("UNBECOME", TestActor);  //Switch back to state2
            actor.Tell("hello", TestActor);

            //Then
            ExpectMsg((object) "string2:hello");
        }

        [Fact]
        public void Given_actor_that_has_called_Become_at_construction_time_When_it_calls_Unbecome_Then_it_switches_back_handler()
        {
            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<BecomeDirectlyInConstructorActor>("become");

            //When
            actor.Tell("hello", TestActor);
            //Then
            ExpectMsg((object) "string3:hello");

            //When
            actor.Tell("UNBECOME", TestActor);  //Switch back to state2
            actor.Tell("hello", TestActor);
            //Then
            ExpectMsg((object) "string2:hello");

            //When
            actor.Tell("UNBECOME", TestActor);  //Switch back to state1
            actor.Tell("hello", TestActor);
            //Then
            ExpectMsg((object) "string1:hello");

            //When
            actor.Tell("UNBECOME", TestActor);  //should still be in state1
            actor.Tell("hello", TestActor);
            //Then
            ExpectMsg((object) "string1:hello");
        }

        private class BecomeActor : ReceiveActor
        {
            public BecomeActor()
            {
                Receive<string>(s => s == "UNBECOME", __ => UnbecomeStacked());
                Receive<string>(s => s == "BECOME", _ => BecomeStacked(State2));
                Receive<string>(s => Sender.Tell("string1:" + s, Self));
                Receive<int>(i => Sender.Tell("int1:" + i, Self));
            }

            private void State2()
            {
                Receive<string>(s => s == "UNBECOME", __ => UnbecomeStacked());
                Receive<string>(s => s == "BECOME", _ => BecomeStacked(State3));
                Receive<string>(s => Sender.Tell("string2:" + s, Self));
            }

            private void State3()
            {
                Receive<string>(s => s == "UNBECOME", __ => UnbecomeStacked());
                Receive<string>(s => Sender.Tell("string3:" + s, Self));
            }
        }

        private class BecomeDirectlyInConstructorActor : ReceiveActor
        {
            public BecomeDirectlyInConstructorActor()
            {
                Receive<string>(s => s == "UNBECOME", __ => UnbecomeStacked());
                Receive<string>(s => s == "BECOME", _ => BecomeStacked(State2));
                Receive<string>(s => Sender.Tell("string1:" + s, Self));
                Receive<int>(i => Sender.Tell("int1:" + i, Self));
                BecomeStacked(State2);
                BecomeStacked(State3);
            }

            private void State2()
            {
                Receive<string>(s => s == "UNBECOME", __ => UnbecomeStacked());
                Receive<string>(s => s == "BECOME", _ => BecomeStacked(State3));
                Receive<string>(s => Sender.Tell("string2:" + s, Self));
            }

            private void State3()
            {
                Receive<string>(s => s == "UNBECOME", __ => UnbecomeStacked());
                Receive<string>(s => Sender.Tell("string3:" + s, Self));
            }
        }

    }
}

