//-----------------------------------------------------------------------
// <copyright file="ReceivePersistentActorTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Persistence.Tests
{

    public partial class ReceivePersistentActorTests : AkkaSpec
    {
        public ReceivePersistentActorTests(ITestOutputHelper output = null)
            : base("akka.persistence.journal.plugin = \"akka.persistence.journal.inmem\"", output)
        {
        }

        [Fact]
        public void Given_persistent_actor_with_no_receive_command_specified_When_receiving_message_Then_it_should_be_unhandled()
        {
            //Given
            var pid = "p-1";
            WriteEvents(pid, 1, 2, 3);
            Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
            var actor = Sys.ActorOf(Props.Create(() => new NoCommandActor(pid)), "no-receive-specified");
          
            //When
            actor.Tell("Something");

            //Then
            ExpectMsg<UnhandledMessage>(m => ((string)m.Message) == "Something" && Equals(m.Recipient, actor));
            Sys.EventStream.Unsubscribe(TestActor, typeof(UnhandledMessage));
        }

        [Fact]
        public void Given_persistent_actor_with_no_receive_event_specified_When_receiving_message_Then_it_should_be_unhandled()
        {
            //Given
            var pid = "p-2";
            WriteEvents(pid, "Something");

            // when
            Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
            var actor = Sys.ActorOf(Props.Create(() => new NoEventActor(pid)), "no-receive-specified");
            
            //Then
            ExpectMsg<UnhandledMessage>(m => ((string)m.Message) == "Something" && Equals(m.Recipient, actor));
            Sys.EventStream.Unsubscribe(TestActor, typeof(UnhandledMessage));
        }

        [Fact]
        public void Test_that_persistent_actor_cannot_call_receive_command_or_receive_event_out_of_construction_and_become()
        {
            //Given
            var pid = "p-3";
            WriteEvents(pid, 1, 2, 3);
            var actor = Sys.ActorOf(Props.Create(() => new CallReceiveWhenHandlingMessageActor(pid)),"receive-on-handling-message");

            //When
            actor.Tell("Something that will trigger the actor do call Receive", TestActor);

            //Then
            //We expect a exception was thrown when the actor called Receive, and that it was sent back to us
            ExpectMsg<InvalidOperationException>();
        }

        [Fact]
        public void Given_a_persistent_actor_which_uses_predicates_When_sending_different_messages_Then_correct_handler_should_be_invoked()
        {
            //Given
            var pid = "p-4";
            WriteEvents(pid, 1, 2, 3);
            var actor = Sys.ActorOf(Props.Create(() => new IntPredicatesActor(pid)) , "predicates");

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
        public void Given_a_persistent_actor_that_uses_non_generic_and_predicates_When_sending_different_messages_Then_correct_handler_should_be_invoked()
        {
            //Given
            var pid = "p-5";
            WriteEvents(pid, 1, 2, 3);
            var actor = Sys.ActorOf(Props.Create(() => new TypePredicatesActor(pid)) , "predicates");

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
        public void Given_a_persistent_actor_with_ReceiveAnyCommand_When_sending_different_messages_Then_correct_handler_should_be_invoked()
        {
            //Given
            var pid = "p-6";
            WriteEvents(pid, 1, 2, 3);
            var actor = Sys.ActorOf(Props.Create(() => new ReceiveAnyActor(pid)) , "matchany");

            //When
            actor.Tell(4711, TestActor);
            actor.Tell("hello", TestActor);

            //Then
            ExpectMsg((object)"int:4711");
            ExpectMsg((object)"any:hello");
        }

        // The following tests establish, for Command/Recover (which share the ReceiveActorHandlers
        // "no more handlers" rule with ReceiveActor), the same v1.5.71 parity restored for ReceiveActor:
        // only an always-handling (Action<T>-based, including CommandAsync/RecoverAsync) object/no-predicate
        // registration blocks later registrations. A Func<T,bool> one may decline (return false) and does
        // not. See https://github.com/akkadotnet/akka.net/pull/7557.

        [Fact(DisplayName = "Should_FallThroughToLaterHandler_When_GenericObjectFuncCommandHandlerWithNoPredicate_Declines")]
        public async Task Should_FallThroughToLaterHandler_When_GenericObjectFuncCommandHandlerWithNoPredicate_Declines()
        {
            var pid = "command-generic-func-object-declines";
            var actor = Sys.ActorOf(Props.Create(() => new GenericObjectFuncCommandDeclinesActor(pid)), "command-generic-func-object-declines");

            actor.Tell("hello", TestActor);
            actor.Tell(42, TestActor);

            await ExpectMsgAsync("string:hello");
            await ExpectMsgAsync("int:42");
        }

        [Fact(DisplayName = "Should_FallThroughToLaterHandler_When_TypedObjectFuncCommandHandlerWithNoPredicate_Declines")]
        public async Task Should_FallThroughToLaterHandler_When_TypedObjectFuncCommandHandlerWithNoPredicate_Declines()
        {
            var pid = "command-typed-func-object-declines";
            var actor = Sys.ActorOf(Props.Create(() => new TypedObjectFuncCommandDeclinesActor(pid)), "command-typed-func-object-declines");

            actor.Tell("hello", TestActor);
            actor.Tell(42, TestActor);

            await ExpectMsgAsync("string:hello");
            await ExpectMsgAsync("int:42");
        }

        [Fact(DisplayName = "Should_FallThroughToLaterHandler_When_GenericObjectFuncRecoverHandlerWithNoPredicate_Declines")]
        public async Task Should_FallThroughToLaterHandler_When_GenericObjectFuncRecoverHandlerWithNoPredicate_Declines()
        {
            var pid = "recover-generic-func-object-declines";
            WriteEvents(pid, 1, "two");
            var actor = Sys.ActorOf(Props.Create(() => new GenericObjectFuncRecoverDeclinesActor(pid)), "recover-generic-func-object-declines");

            actor.Tell("GetState", TestActor);

            await ExpectMsgAsync("int:1");
            await ExpectMsgAsync("string:two");
        }

        [Fact(DisplayName = "Should_FallThroughToLaterHandler_When_TypedObjectFuncRecoverHandlerWithNoPredicate_Declines")]
        public async Task Should_FallThroughToLaterHandler_When_TypedObjectFuncRecoverHandlerWithNoPredicate_Declines()
        {
            var pid = "recover-typed-func-object-declines";
            WriteEvents(pid, 1, "two");
            var actor = Sys.ActorOf(Props.Create(() => new TypedObjectFuncRecoverDeclinesActor(pid)), "recover-typed-func-object-declines");

            actor.Tell("GetState", TestActor);

            await ExpectMsgAsync("int:1");
            await ExpectMsgAsync("string:two");
        }

        [Fact(DisplayName = "Should_ThrowActorInitializationException_WithInvalidOperationExceptionInner_When_RegisteringCommandHandler_After_GenericObjectActionCommandHandlerWithNoPredicate")]
        public async Task Should_Throw_When_RegisteringCommandHandler_After_GenericObjectActionCommandHandlerWithNoPredicate()
        {
            Sys.EventStream.Subscribe(TestActor, typeof(Error));

            var pid = "command-generic-action-object-blocks";
            var actor = Sys.ActorOf(Props.Create(() => new GenericObjectActionCommandThenAnotherActor(pid)), "command-generic-action-object-blocks");

            await AssertActorInitializationFailureAsync(actor);

            Sys.EventStream.Unsubscribe(TestActor, typeof(Error));
        }

        [Fact(DisplayName = "Should_ThrowActorInitializationException_WithInvalidOperationExceptionInner_When_RegisteringRecoverHandler_After_GenericObjectActionRecoverHandlerWithNoPredicate")]
        public async Task Should_Throw_When_RegisteringRecoverHandler_After_GenericObjectActionRecoverHandlerWithNoPredicate()
        {
            Sys.EventStream.Subscribe(TestActor, typeof(Error));

            var pid = "recover-generic-action-object-blocks";
            var actor = Sys.ActorOf(Props.Create(() => new GenericObjectActionRecoverThenAnotherActor(pid)), "recover-generic-action-object-blocks");

            await AssertActorInitializationFailureAsync(actor);

            Sys.EventStream.Unsubscribe(TestActor, typeof(Error));
        }

        [Fact(DisplayName = "Should_ThrowActorInitializationException_WithInvalidOperationExceptionInner_When_RegisteringHandler_After_GenericObjectCommandAsyncHandlerWithNoPredicate")]
        public async Task Should_Throw_When_RegisteringHandler_After_GenericObjectCommandAsyncHandlerWithNoPredicate()
        {
            Sys.EventStream.Subscribe(TestActor, typeof(Error));

            var pid = "command-async-generic-object-blocks";
            var actor = Sys.ActorOf(Props.Create(() => new GenericObjectAsyncCommandThenAnotherActor(pid)), "command-async-generic-object-blocks");

            await AssertActorInitializationFailureAsync(actor);

            Sys.EventStream.Unsubscribe(TestActor, typeof(Error));
        }

        [Fact(DisplayName = "Should_HandleTypedCommand_When_RegisteredInsideBecome")]
        public async Task Should_HandleTypedCommand_When_RegisteredInsideBecome()
        {
            // Regression test for a #7557-era bug: PersistentActor.AddTypedReceiveHandler used to
            // unconditionally call EnsureMayConfigureRecoverHandlers(), even for Command(Type, ...)
            // (isRecover: false). Become/BecomeStacked only push a new frame onto _matchCommandBuilders,
            // so _matchRecoverBuilders is empty while inside Become - meaning Command(typeof(X), ...)
            // called from inside Become incorrectly threw "You may only call Recover-methods...".
            var pid = "command-typed-inside-become";
            var actor = Sys.ActorOf(Props.Create(() => new CommandTypedInsideBecomeActor(pid)), "command-typed-inside-become");

            actor.Tell("BECOME", TestActor);
            actor.Tell(42, TestActor);

            await ExpectMsgAsync("int2:42");
        }

        /// <summary>
        /// Expects a single <see cref="Error"/> log event caused by an <see cref="ActorInitializationException"/>
        /// whose inner-exception chain contains an <see cref="InvalidOperationException"/> (the actor system
        /// wraps actor-construction failures in several layers - reflection-based activation adds its own
        /// wrapping on top of ActorInitializationException), and that <paramref name="actor"/> subsequently
        /// terminates (the default supervisor strategy's directive for ActorInitializationException is Stop).
        /// </summary>
        private async Task AssertActorInitializationFailureAsync(IActorRef actor)
        {
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
        }

        private static TException FindInnerException<TException>(Exception exception) where TException : Exception
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is TException match)
                    return match;
            }

            return null;
        }

        private readonly AtomicCounterLong _seqNrCounter = new(1L);
        /// <summary>
        /// Initialize test journal using provided events.
        /// </summary>
        private void WriteEvents(string pid, params object[] events)
        {
            var journalRef = Persistence.Instance.Apply(Sys).JournalFor(string.Empty);
            var persistents = events
                .Select(e => new Persistent(e, _seqNrCounter.GetAndIncrement(), pid, e.GetType().FullName))
                .ToArray();
            journalRef.Tell(new WriteMessages(persistents.Select(p => new AtomicWrite(p)), TestActor, 1));

            ExpectMsg<WriteMessagesSuccessful>();
            foreach (var p in persistents)
                ExpectMsg(new WriteMessageSuccess(p, 1));
        }

        private abstract class TestReceivePersistentActor : ReceivePersistentActor
        {
            public readonly LinkedList<object> State = new();
            private readonly string _persistenceId;

            protected TestReceivePersistentActor(string persistenceId)
            {
                _persistenceId = persistenceId;
            }

            public override string PersistenceId { get { return _persistenceId; } }
        }

        private class NoCommandActor : TestReceivePersistentActor
        {
            public NoCommandActor(string pid) : base(pid)
            {
                RecoverAny(o => State.AddLast(o));
                // no command here
            }
        }

        private class NoEventActor : TestReceivePersistentActor
        {
            public NoEventActor(string pid) : base(pid)
            {
                CommandAny(msg => Sender.Tell(msg, Self));
                // no recover here
            }
        }

        private class CallReceiveWhenHandlingMessageActor : TestReceivePersistentActor
        {
            public CallReceiveWhenHandlingMessageActor(string pid) : base(pid)
            {
                Recover<int>(i => State.AddLast(i));
                Command<object>(_ =>
                {
                    try
                    {
                        Command<int>(i => Sender.Tell(i, Self));
                        Sender.Tell(null, Self);
                    }
                    catch(Exception e)
                    {
                        Sender.Tell(e, Self);
                    }
                });
            }
        }

        private class IntPredicatesActor : TestReceivePersistentActor
        {
            public IntPredicatesActor(string pid) : base(pid)
            {
                Recover<int>(i => State.AddLast(i));
                Command<int>(i => i < 5, i => Sender.Tell("int<5:" + i, Self));     //Predicate first, when i < 5
                Command<int>(i => Sender.Tell("int<10:" + i, Self), i => i < 10);   //Predicate after, when 5 <= i < 10
                Command<int>(i =>
                {
                    if(i < 15)
                    {
                        Sender.Tell("int<15:" + i, Self);
                        return true;
                    }
                    return false;
                });                                                                 //Func,            when 10 <= i < 15
                Command<int>(i => Sender.Tell("int:" + i, Self), null);             //Null predicate,  when i >= 15
                Command<int>(i => Sender.Tell("ShouldNeverMatch:" + i, Self));      //The handler above should never be invoked
            }
        }

        private class TypePredicatesActor : TestReceivePersistentActor
        {
            public TypePredicatesActor(string pid) : base(pid)
            {
                Recover<int>(i => State.AddLast(i));
                Command(typeof(int), i => (int)i < 5, i => Sender.Tell("int<5:" + i, Self));     //Predicate first, when i < 5
                Command(typeof(int), i => Sender.Tell("int<10:" + i, Self), i => (int)i < 10);   //Predicate after, when 5 <= i < 10
                Command(typeof(int), o =>
                {
                    var i = (int) o;
                    if(i < 15)
                    {
                        Sender.Tell("int<15:" + i, Self);
                        return true;
                    }
                    return false;
                });                                                                              //Func,            when 10 <= i < 15
                Command(typeof(int), i => Sender.Tell("int:" + i, Self), null);                  //Null predicate,  when i >= 15
                Command(typeof(int), i => Sender.Tell("ShouldNeverMatch:" + i, Self));           //The handler above should never be invoked
                Command(typeof(string), i => Sender.Tell("string:" + i));
            }
        }


        private class ReceiveAnyActor : TestReceivePersistentActor
        {
            public ReceiveAnyActor(string pid) : base(pid)
            {
                Command<int>(i => Sender.Tell("int:" + i, Self));
                CommandAny(o => Sender.Tell("any:" + o, Self));
            }
        }

        private class GenericObjectFuncCommandDeclinesActor : TestReceivePersistentActor
        {
            public GenericObjectFuncCommandDeclinesActor(string pid) : base(pid)
            {
                Command<object>(o =>
                {
                    if (o is string s)
                    {
                        Sender.Tell("string:" + s, Self);
                        return true;
                    }

                    return false; // decline - later handlers should still get a chance to run
                });
                Command<int>(i => Sender.Tell("int:" + i, Self));
            }
        }

        private class TypedObjectFuncCommandDeclinesActor : TestReceivePersistentActor
        {
            public TypedObjectFuncCommandDeclinesActor(string pid) : base(pid)
            {
                Command(typeof(object), o =>
                {
                    if (o is string s)
                    {
                        Sender.Tell("string:" + s, Self);
                        return true;
                    }

                    return false; // decline - later handlers should still get a chance to run
                });
                Command<int>(i => Sender.Tell("int:" + i, Self));
            }
        }

        private class GenericObjectFuncRecoverDeclinesActor : TestReceivePersistentActor
        {
            public GenericObjectFuncRecoverDeclinesActor(string pid) : base(pid)
            {
                Recover<object>(o =>
                {
                    if (o is int i)
                    {
                        State.AddLast("int:" + i);
                        return true;
                    }

                    return false; // decline - later handlers should still get a chance to run
                });
                Recover<string>(s => State.AddLast("string:" + s));
                Command<string>(s => s == "GetState", _ =>
                {
                    foreach (var item in State)
                        Sender.Tell(item, Self);
                });
            }
        }

        private class TypedObjectFuncRecoverDeclinesActor : TestReceivePersistentActor
        {
            public TypedObjectFuncRecoverDeclinesActor(string pid) : base(pid)
            {
                Recover(typeof(object), o =>
                {
                    if (o is int i)
                    {
                        State.AddLast("int:" + i);
                        return true;
                    }

                    return false; // decline - later handlers should still get a chance to run
                });
                Recover<string>(s => State.AddLast("string:" + s));
                Command<string>(s => s == "GetState", _ =>
                {
                    foreach (var item in State)
                        Sender.Tell(item, Self);
                });
            }
        }

        private class GenericObjectActionCommandThenAnotherActor : TestReceivePersistentActor
        {
            public GenericObjectActionCommandThenAnotherActor(string pid) : base(pid)
            {
                Command<object>(_ => { });
                Command<string>(_ => { }); // should throw - no more handlers can be added
            }
        }

        private class GenericObjectActionRecoverThenAnotherActor : TestReceivePersistentActor
        {
            public GenericObjectActionRecoverThenAnotherActor(string pid) : base(pid)
            {
                Recover<object>(_ => { });
                Recover<int>(_ => { }); // should throw - no more handlers can be added
            }
        }

        private class GenericObjectAsyncCommandThenAnotherActor : TestReceivePersistentActor
        {
            public GenericObjectAsyncCommandThenAnotherActor(string pid) : base(pid)
            {
                CommandAsync<object>(_ => Task.CompletedTask);
                Command<string>(_ => { }); // should throw - no more handlers can be added
            }
        }

        private class CommandTypedInsideBecomeActor : TestReceivePersistentActor
        {
            public CommandTypedInsideBecomeActor(string pid) : base(pid)
            {
                Command<string>(s => s == "BECOME", _ => Become(State2));
                Command<string>(s => Sender.Tell("string1:" + s, Self));
            }

            private void State2()
            {
                Command(typeof(int), o => Sender.Tell("int2:" + o, Self));
            }
        }

    }
}

