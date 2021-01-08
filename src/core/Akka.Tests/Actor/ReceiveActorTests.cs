//-----------------------------------------------------------------------
// <copyright file="ReceiveActorTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Xunit;


namespace Akka.Tests.Actor
{

    public partial class ReceiveActorTests : AkkaSpec
    {
        [Fact]
        public void Given_actor_with_no_receive_specified_When_receiving_message_Then_it_should_be_unhandled()
        {
            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<NoReceiveActor>("no-receive-specified");
            system.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

            //When
            actor.Tell("Something");

            //Then
            ExpectMsg<UnhandledMessage>(m => ((string)m.Message) == "Something" && m.Recipient == actor);
            system.EventStream.Unsubscribe(TestActor, typeof(UnhandledMessage));
        }


        [Fact]
        public void Test_that_actor_cannot_call_receive_out_of_construction_and_become()
        {
            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<CallReceiveWhenHandlingMessageActor>("receive-on-handling-message");

            //When
            actor.Tell("Something that will trigger the actor do call Receive", TestActor);

            //Then
            //We expect a exception was thrown when the actor called Receive, and that it was sent back to us
            ExpectMsg<InvalidOperationException>();
        }

        [Fact]
        public void Given_an_EchoActor_When_receiving_messages_Then_messages_should_be_sent_back()
        {
            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<EchoReceiveActor>("no-receive-specified");

            //When
            actor.Tell("Something", TestActor);
            actor.Tell("Something else", TestActor);

            //Then
            ExpectMsg((object) "Something");
            ExpectMsg((object) "Something else");
        }

        [Fact]
        public void Given_an_actor_which_uses_predicates_When_sending_different_messages_Then_correct_handler_should_be_invoked()
        {
            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<IntPredicatesActor>("predicates");

            //When
            actor.Tell(0, TestActor);
            actor.Tell(5, TestActor);
            actor.Tell(10, TestActor);
            actor.Tell(15, TestActor);

            //Then
            ExpectMsg((object) "int<5:0");
            ExpectMsg((object) "int<10:5");
            ExpectMsg((object) "int<15:10");
            ExpectMsg((object) "int:15");
        }

        [Fact]
        public void Given_an_actor_that_uses_non_generic_and_predicates_When_sending_different_messages_Then_correct_handler_should_be_invoked()
        {
            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<TypePredicatesActor>("predicates");

            //When
            actor.Tell(0, TestActor);
            actor.Tell(5, TestActor);
            actor.Tell(10, TestActor);
            actor.Tell(15, TestActor);
            actor.Tell("hello", TestActor);

            //Then
            ExpectMsg((object) "int<5:0");
            ExpectMsg((object) "int<10:5");
            ExpectMsg((object) "int<15:10");
            ExpectMsg((object) "int:15");
            ExpectMsg((object) "string:hello");
        }


        [Fact]
        public void Given_an_actor_with_ReceiveAny_When_sending_different_messages_Then_correct_handler_should_be_invoked()
        {
            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<ReceiveAnyActor>("matchany");

            //When
            actor.Tell(4711, TestActor);
            actor.Tell("hello", TestActor);

            //Then
            ExpectMsg((object)"int:4711");
            ExpectMsg((object)"any:hello");
        }

        [Fact]
        public void Given_an_actor_which_overrides_PreStart_When_sending_a_message_Then_the_message_should_be_handled()
        {
            //Given
            var actor = Sys.ActorOf<PreStartEchoReceiveActor>("echo");

            //When
            actor.Tell(4711, TestActor);

            //Then
            ExpectMsg(4711);
        }

        private class NoReceiveActor : ReceiveActor
        {
        }

        private class EchoReceiveActor : ReceiveActor
        {
            public EchoReceiveActor()
            {
                Receive<object>(msg => Sender.Tell(msg, Self));
            }
        }


        private class PreStartEchoReceiveActor : ReceiveActor
        {
            public PreStartEchoReceiveActor()
            {
                Receive<object>(msg => Sender.Tell(msg, Self));
            }

            protected override void PreStart()
            {
                //Just here to make sure base.PreStart isn't called
            }
        }

        private class CallReceiveWhenHandlingMessageActor : ReceiveActor
        {
            public CallReceiveWhenHandlingMessageActor()
            {
                Receive<object>(m =>
                {
                    try
                    {
                        Receive<int>(i => Sender.Tell(i, Self));
                        Sender.Tell(null, Self);
                    }
                    catch(Exception e)
                    {
                        Sender.Tell(e, Self);
                    }
                });
            }
        }

        private class IntPredicatesActor : ReceiveActor
        {
            public IntPredicatesActor()
            {
                Receive<int>(i => i < 5, i => Sender.Tell("int<5:" + i, Self));     //Predicate first, when i < 5
                Receive<int>(i => Sender.Tell("int<10:" + i, Self), i => i < 10);   //Predicate after, when 5 <= i < 10
                Receive<int>(i =>
                {
                    if(i < 15)
                    {
                        Sender.Tell("int<15:" + i, Self);
                        return true;
                    }
                    return false;
                });                                                                 //Func,            when 10 <= i < 15
                Receive<int>(i => Sender.Tell("int:" + i, Self), null);             //Null predicate,  when i >= 15
                Receive<int>(i => Sender.Tell("ShouldNeverMatch:" + i, Self));      //The handler above should never be invoked
            }
        }

        private class TypePredicatesActor : ReceiveActor
        {
            public TypePredicatesActor()
            {
                Receive(typeof(int), i => (int)i < 5, i => Sender.Tell("int<5:" + i, Self));     //Predicate first, when i < 5
                Receive(typeof(int), i => Sender.Tell("int<10:" + i, Self), i => (int)i < 10);   //Predicate after, when 5 <= i < 10
                Receive(typeof(int), o =>
                {
                    var i = (int) o;
                    if(i < 15)
                    {
                        Sender.Tell("int<15:" + i, Self);
                        return true;
                    }
                    return false;
                });                                                                              //Func,            when 10 <= i < 15
                Receive(typeof(int), i => Sender.Tell("int:" + i, Self), null);                  //Null predicate,  when i >= 15
                Receive(typeof(int), i => Sender.Tell("ShouldNeverMatch:" + i, Self));           //The handler above should never be invoked
                Receive(typeof(string), i => Sender.Tell("string:" + i));
            }
        }


        private class ReceiveAnyActor : ReceiveActor
        {
            public ReceiveAnyActor()
            {
                Receive<int>(i => Sender.Tell("int:" + i, Self));
                ReceiveAny(o =>
                {
                    Sender.Tell("any:" + o, Self);
                });
            }
        }

    }
}

