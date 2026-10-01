//-----------------------------------------------------------------------
// <copyright file="ReceiveActorTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Xunit;


namespace Akka.Tests.Actor
{

    public partial class ReceiveActorTests : AkkaSpec
    {
        public ReceiveActorTests(ITestOutputHelper output = null)
            : base((Config)null, output)
        {
        }

        [Fact]
        public async Task Given_actor_with_no_receive_specified_When_receiving_message_Then_it_should_be_unhandled()
        {
            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<NoReceiveActor>("no-receive-specified");
            system.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

            //When
            actor.Tell("Something");

            //Then
            await ExpectMsgAsync<UnhandledMessage>(m => ((string)m.Message) == "Something" && m.Recipient == actor);
            system.EventStream.Unsubscribe(TestActor, typeof(UnhandledMessage));
        }


        [Fact]
        public async Task Test_that_actor_cannot_call_receive_out_of_construction_and_become()
        {
            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<CallReceiveWhenHandlingMessageActor>("receive-on-handling-message");

            //When
            actor.Tell("Something that will trigger the actor do call Receive", TestActor);

            //Then
            //We expect a exception was thrown when the actor called Receive, and that it was sent back to us
            await ExpectMsgAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task Given_an_EchoActor_When_receiving_messages_Then_messages_should_be_sent_back()
        {
            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<EchoReceiveActor>("no-receive-specified");

            //When
            actor.Tell("Something", TestActor);
            actor.Tell("Something else", TestActor);

            //Then
            await ExpectMsgAsync((object) "Something");
            await ExpectMsgAsync((object) "Something else");
        }

        [Fact]
        public async Task Given_an_actor_which_uses_predicates_When_sending_different_messages_Then_correct_handler_should_be_invoked()
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
            await ExpectMsgAsync((object) "int<5:0");
            await ExpectMsgAsync((object) "int<10:5");
            await ExpectMsgAsync((object) "int<15:10");
            await ExpectMsgAsync((object) "int:15");
        }

        [Fact]
        public async Task Given_an_actor_that_uses_non_generic_and_predicates_When_sending_different_messages_Then_correct_handler_should_be_invoked()
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
            await ExpectMsgAsync((object) "int<5:0");
            await ExpectMsgAsync((object) "int<10:5");
            await ExpectMsgAsync((object) "int<15:10");
            await ExpectMsgAsync((object) "int:15");
            await ExpectMsgAsync((object) "string:hello");
        }


        [Fact]
        public async Task Given_an_actor_with_ReceiveAny_When_sending_different_messages_Then_correct_handler_should_be_invoked()
        {
            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<ReceiveAnyActor>("matchany");

            //When
            actor.Tell(4711, TestActor);
            actor.Tell("hello", TestActor);

            //Then
            await ExpectMsgAsync((object)"int:4711");
            await ExpectMsgAsync((object)"any:hello");
        }

        [Fact]
        public async Task Given_an_actor_which_overrides_PreStart_When_sending_a_message_Then_the_message_should_be_handled()
        {
            //Given
            var actor = Sys.ActorOf<PreStartEchoReceiveActor>("echo");

            //When
            actor.Tell(4711, TestActor);

            //Then
            await ExpectMsgAsync(4711);
        }

        [Fact]
        public async Task Given_an_actor_which_adds_any_handler_twice_should_throw_exception()
        {
            // Handling the scenario where the actor adds an any handler twice. This should not be allowed.
            // Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<AnyAddedTwiceActor>("addedtwice");
            
            // When
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var terminated =  await actor.WatchAsync(cts.Token);

            // Then
            Assert.True(terminated);
        }
        
        [Fact]
        public async Task Actor_Can_Handle_Message_When_Base_Is_Defined_In_Receive()
        {
            //Given
            var actor = Sys.ActorOf<ReceiveCanHandleBaseTypesActor>("ReceiveCanHandleBaseTypes");

            //When
            actor.Tell(new ReceiveCanHandleBaseMessage(), TestActor);

            //Then
            await ExpectMsgAsync("Handled");
        }

        [Fact(DisplayName = "Should_FallThroughToLaterHandler_When_ObjectFuncHandlerWithNoPredicate_Declines")]
        public async Task Should_FallThroughToLaterHandler_When_ObjectFuncHandlerWithNoPredicate_Declines()
        {
            // Regression test for the #7557 rewrite: a Receive(typeof(object), Func<object,bool>) handler
            // with no predicate may decline (return false) for a given message. In that case later
            // handlers must still get a chance to run - matching v1.5.71's MatchBuilder-based behavior -
            // instead of the registration itself being rejected or the message being left unhandled.
            var actor = Sys.ActorOf<ObjectFuncHandlerDeclinesActor>("object-func-handler-declines");

            actor.Tell(42, TestActor);
            actor.Tell("hello", TestActor);

            await ExpectMsgAsync("int:42");
            await ExpectMsgAsync("string:hello");
        }

        [Fact(DisplayName = "Should_FallThroughToLaterHandler_When_GenericObjectFuncHandlerWithNoPredicate_Declines")]
        public async Task Should_FallThroughToLaterHandler_When_GenericObjectFuncHandlerWithNoPredicate_Declines()
        {
            // Companion test for the generic Receive<object>(Func<object,bool>) overload (ReceiveActor.cs
            // ~312): it may decline (return false) for a given message, so later handlers - here a
            // Receive<int> - must still get a chance to run, matching v1.5.71.
            var actor = Sys.ActorOf<GenericObjectFuncHandlerDeclinesActor>("generic-object-func-handler-declines");

            actor.Tell("hello", TestActor);
            actor.Tell(42, TestActor);

            await ExpectMsgAsync("string:hello");
            await ExpectMsgAsync("int:42");
        }

        [Fact(DisplayName = "Should_ThrowActorInitializationException_WithInvalidOperationExceptionInner_When_RegisteringHandler_After_ObjectActionHandlerWithNoPredicate")]
        public async Task Should_Throw_When_RegisteringHandler_After_ObjectActionHandlerWithNoPredicate()
        {
            // Sanity-check companion to the tests above: an always-handling object handler (what the
            // Action<object>-based Receive overload produces) with no predicate must still block later
            // registrations, exactly as in v1.5.71 and as a ReceiveAny handler does. The actor's
            // constructor throws InvalidOperationException, which the actor system wraps in an
            // ActorInitializationException and reports as an Error log event before stopping the actor
            // (ActorInitializationException is "Stop", never "Restart", in the default supervisor strategy).
            Sys.EventStream.Subscribe(TestActor, typeof(Error));

            var actor = Sys.ActorOf<ObjectActionHandlerThenAnotherHandlerActor>("object-action-handler-then-another");

            var error = await ExpectMsgAsync<Error>();
            var initEx = Assert.IsType<ActorInitializationException>(error.Cause);
            var invalidOpEx = FindInnerException<InvalidOperationException>(initEx);
            Assert.NotNull(invalidOpEx);
            Assert.Equal(
                "A handler that catches all messages has been added. No handler can be added after that.",
                invalidOpEx.Message);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var terminated = await actor.WatchAsync(cts.Token);
            Assert.True(terminated);

            Sys.EventStream.Unsubscribe(TestActor, typeof(Error));
        }

        [Fact(DisplayName = "Should_ThrowActorInitializationException_WithInvalidOperationExceptionInner_When_RegisteringHandler_After_GenericObjectActionHandlerWithNoPredicate")]
        public async Task Should_Throw_When_RegisteringHandler_After_GenericObjectActionHandlerWithNoPredicate()
        {
            // Companion test for the generic Receive<object>(Action<object>) overload, which must also
            // block later registrations when no predicate is supplied - matching v1.5.71. (Prior to this
            // fix, the generic path never blocked later registrations regardless of T.)
            Sys.EventStream.Subscribe(TestActor, typeof(Error));

            var actor = Sys.ActorOf<GenericObjectActionHandlerThenAnotherHandlerActor>("generic-object-action-handler-then-another");

            var error = await ExpectMsgAsync<Error>();
            var initEx = Assert.IsType<ActorInitializationException>(error.Cause);
            var invalidOpEx = FindInnerException<InvalidOperationException>(initEx);
            Assert.NotNull(invalidOpEx);
            Assert.Equal(
                "A handler that catches all messages has been added. No handler can be added after that.",
                invalidOpEx.Message);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var terminated = await actor.WatchAsync(cts.Token);
            Assert.True(terminated);

            Sys.EventStream.Unsubscribe(TestActor, typeof(Error));
        }

        /// <summary>
        /// Walks the <see cref="Exception.InnerException"/> chain (actor construction failures get wrapped
        /// in a <see cref="TypeLoadException"/>/<see cref="System.Reflection.TargetInvocationException"/> by
        /// the reflection-based activator before <see cref="ActorInitializationException"/> wraps all of
        /// that) and returns the first exception assignable to <typeparamref name="TException"/>, or
        /// <c>null</c> if none is found.
        /// </summary>
        private static TException FindInnerException<TException>(Exception exception) where TException : Exception
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is TException match)
                    return match;
            }

            return null;
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
                Receive<object>(_ =>
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

        private class AnyAddedTwiceActor : ReceiveActor
        {
            public AnyAddedTwiceActor()
            {
                ReceiveAny(o =>
                {
                    Sender.Tell("any:" + o, Self);
                });
                ReceiveAny(o =>
                {
                    Sender.Tell("not allowed any:" + o, Self);
                });
            }
        }

        private class ReceiveCanHandleBaseTypesActor : ReceiveActor
        {
            public ReceiveCanHandleBaseTypesActor()
            {
                Receive<IReceiveCanHandleBaseMessage>(i => Sender.Tell("Handled", Self));
            }
        }

        private class ObjectFuncHandlerDeclinesActor : ReceiveActor
        {
            public ObjectFuncHandlerDeclinesActor()
            {
                Receive(typeof(object), o =>
                {
                    if (o is int i)
                    {
                        Sender.Tell("int:" + i, Self);
                        return true;
                    }

                    return false; // decline - later handlers should still get a chance to run
                });
                Receive<string>(s => Sender.Tell("string:" + s, Self));
            }
        }

        private class GenericObjectFuncHandlerDeclinesActor : ReceiveActor
        {
            public GenericObjectFuncHandlerDeclinesActor()
            {
                Receive<object>(o =>
                {
                    if (o is string s)
                    {
                        Sender.Tell("string:" + s, Self);
                        return true;
                    }

                    return false; // decline - later handlers should still get a chance to run
                });
                Receive<int>(i => Sender.Tell("int:" + i, Self));
            }
        }

        private class ObjectActionHandlerThenAnotherHandlerActor : ReceiveActor
        {
            public ObjectActionHandlerThenAnotherHandlerActor()
            {
                Receive(typeof(object), _ => { });
                Receive<string>(_ => { }); // should throw - no more handlers can be added
            }
        }

        private class GenericObjectActionHandlerThenAnotherHandlerActor : ReceiveActor
        {
            public GenericObjectActionHandlerThenAnotherHandlerActor()
            {
                Receive<object>(_ => { });
                Receive<string>(_ => { }); // should throw - no more handlers can be added
            }
        }

        private record ReceiveCanHandleBaseMessage : IReceiveCanHandleBaseMessage { }

        private interface IReceiveCanHandleBaseMessage { }
    }
}

