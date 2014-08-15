using System;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Xunit;


namespace Akka.Tests.Actor
{

    public partial class ReceiveActorTests : AkkaSpec
    {
        private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(2);

        [Fact]
        public void Given_actor_with_no_receive_specified_When_receiving_message_Then_it_should_be_unhandled()
        {
            //Given
            var system = new ActorSystem("test");
            var actor = system.ActorOf<NoReceiveActor>("no-receive-specified");
            system.EventStream.Subscribe(testActor, typeof(UnhandledMessage));

            //When
            actor.Tell("Something");

            //Then
            expectMsg<UnhandledMessage>(m => ((string)m.Message) == "Something" && m.Recipient == actor, _defaultTimeout);
            system.EventStream.Unsubscribe(testActor, typeof(UnhandledMessage));
        }


        [Fact]
        public void Test_that_actor_cannot_call_receive_out_of_construction_and_become()
        {
            //Given
            var system = new ActorSystem("test");
            var actor = system.ActorOf<CallReceiveWhenHandlingMessageActor>("receive-on-handling-message");

            //When
            actor.Tell("Something that will trigger the actor do call Receive", testActor);

            //Then
            //We expect a exception was thrown when the actor called Receive, and that it was sent back to us
            expectMsg<InvalidOperationException>();
        }

        [Fact]
        public void Given_an_EchoActor_When_receiveing_messages_Then_messages_should_be_sent_back()
        {
            //Given
            var system = new ActorSystem("test");
            var actor = system.ActorOf<EchoReceiveActor>("no-receive-specified");

            //When
            actor.Tell("Something", testActor);
            actor.Tell("Something else", testActor);

            //Then
            expectMsg("Something", _defaultTimeout);
            expectMsg("Something else", _defaultTimeout);
        }

        [Fact]
        public void Given_an_actor_which_uses_predicates_When_sending_different_messages_Then_correct_handler_should_be_invoked()
        {
            //Given
            var system = new ActorSystem("test");
            var actor = system.ActorOf<IntPredicatesActor>("predicates");

            //When
            actor.Tell(0, testActor);
            actor.Tell(5, testActor);
            actor.Tell(10, testActor);
            actor.Tell(15, testActor);

            //Then
            expectMsg("int<5:0", _defaultTimeout);
            expectMsg("int<10:5", _defaultTimeout);
            expectMsg("int<15:10", _defaultTimeout);
            expectMsg("int:15", _defaultTimeout);
        }

        [Fact]
        public void Given_an_actor_that_uses_non_generic_and_predicates_When_sending_different_messages_Then_correct_handler_should_be_invoked()
        {
            //Given
            var system = new ActorSystem("test");
            var actor = system.ActorOf<TypePredicatesActor>("predicates");

            //When
            actor.Tell(0, testActor);
            actor.Tell(5, testActor);
            actor.Tell(10, testActor);
            actor.Tell(15, testActor);
            actor.Tell("hello", testActor);

            //Then
            expectMsg("int<5:0", _defaultTimeout);
            expectMsg("int<10:5", _defaultTimeout);
            expectMsg("int<15:10", _defaultTimeout);
            expectMsg("int:15", _defaultTimeout);
            expectMsg("string:hello", _defaultTimeout);
        }


        [Fact]
        public void Given_an_actor_with_ReceiveAny_When_sending_different_messages_Then_correct_handler_should_be_invoked()
        {
            //Given
            var system = new ActorSystem("test");
            var actor = system.ActorOf<ReceiveAnyActor>("matchany");

            //When
            actor.Tell(4711, testActor);
            actor.Tell("hello", testActor);

            //Then
            expectMsg("int:4711", _defaultTimeout);
            expectMsg("any:hello", _defaultTimeout);
        }

        protected T expectMsg<T>(Func<T, bool> isTheExpected = null)
        {
            return expectMsg<T>(isTheExpected, _defaultTimeout);
        }

        protected T expectMsg<T>(Func<T, bool> isTheExpected, TimeSpan timespan)
        {
            object m;
            if(queue.TryTake(out m, timespan))
            {
                Assert.True(m is T,string.Format("Expected a message of type <{0}>. Actual: <{1}>", typeof(T), m.GetType()));
                var actual = (T)m;
                if(isTheExpected != null)
                    Assert.True(isTheExpected(actual), "The message did not match the predicate.");
                return actual;
            }
            XAssert.Fail(string.Format("Timed out. Expected a message of type {0} within {1}", typeof(T), timespan));
            return default(T);
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
                ReceiveAny(o => Sender.Tell("any:" + o, Self));
            }
        }

    }
}