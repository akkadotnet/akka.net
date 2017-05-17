﻿//-----------------------------------------------------------------------
// <copyright file="ReceivePersistentActorTests_Become.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using Xunit;

namespace Akka.Persistence.Tests
{
    public partial class ReceivePersistentActorTests
    {
        [Fact]
        public void Given_persistent_actor_When_it_calls_Become_Then_it_switches_command_handler()
        {
            //Given
            var pid = "p-21";
            WriteEvents(pid, 1, 2, 3);
            var actor = Sys.ActorOf(Props.Create(() => new BecomeActor(pid)), "become");
            Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

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
        public void Given_persistent_actor_that_has_called_Become_When_it_calls_Unbecome_Then_it_switches_back_command_handler()
        {
            //Given
            var pid = "p-22";
            WriteEvents(pid, 1, 2, 3);
            var actor = Sys.ActorOf(Props.Create(() => new BecomeActor(pid)), "become");
            actor.Tell("BECOME", TestActor);    //Switch to state2
            actor.Tell("BECOME", TestActor);    //Switch to state3

            //When
            actor.Tell("UNBECOME", TestActor);  //Switch back to state2
            actor.Tell("hello", TestActor);

            //Then
            ExpectMsg((object) "string2:hello");
        }

        [Fact]
        public void Given_persistent_actor_that_has_called_Become_at_construction_time_When_it_calls_Unbecome_Then_it_switches_back_command_handler()
        {
            //Given
            var pid = "p-23";
            WriteEvents(pid, 1, 2, 3);
            var actor = Sys.ActorOf(Props.Create(() => new BecomeDirectlyInConstructorActor(pid)), "become");

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

        private class BecomeActor : TestReceivePersistentActor
        {
            public BecomeActor(string pid) : base(pid)
            {
                Recover<int>(i => State.AddLast(i));
                Command<string>(s => s == "UNBECOME", _ => UnbecomeStacked());
                Command<string>(s => s == "BECOME", _ => BecomeStacked(State2));
                Command<string>(s => Sender.Tell("string1:" + s, Self));
                Command<int>(i => Sender.Tell("int1:" + i, Self));
            }

            private void State2()
            {
                Command<string>(s => s == "UNBECOME", __ => UnbecomeStacked());
                Command<string>(s => s == "BECOME", _ => BecomeStacked(State3));
                Command<string>(s => Sender.Tell("string2:" + s, Self));
            }

            private void State3()
            {
                Command<string>(s => s == "UNBECOME", __ => UnbecomeStacked());
                Command<string>(s => Sender.Tell("string3:" + s, Self));
            }
        }

        private class BecomeDirectlyInConstructorActor : TestReceivePersistentActor
        {
            public BecomeDirectlyInConstructorActor(string pid) : base(pid)
            {
                Recover<int>(i => State.AddLast(i));
                Command<string>(s => s == "UNBECOME", __ => UnbecomeStacked());
                Command<string>(s => s == "BECOME", _ => BecomeStacked(State2));
                Command<string>(s => Sender.Tell("string1:" + s, Self));
                Command<int>(i => Sender.Tell("int1:" + i, Self));
                BecomeStacked(State2);
                BecomeStacked(State3);
            }

            private void State2()
            {
                Command<string>(s => s == "UNBECOME", __ => UnbecomeStacked());
                Command<string>(s => s == "BECOME", _ => BecomeStacked(State3));
                Command<string>(s => Sender.Tell("string2:" + s, Self));
            }

            private void State3()
            {
                Command<string>(s => s == "UNBECOME", __ => UnbecomeStacked());
                Command<string>(s => Sender.Tell("string3:" + s, Self));
            }
        }

    }
}

