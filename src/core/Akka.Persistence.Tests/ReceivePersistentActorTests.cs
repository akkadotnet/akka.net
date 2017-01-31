//-----------------------------------------------------------------------
// <copyright file="ReceivePersistentActorTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;

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
            var actor = Sys.ActorOf(Props.Create(() => new NoCommandActor(pid)), "no-receive-specified");
            Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

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
            var actor = Sys.ActorOf(Props.Create(() => new NoEventActor(pid)), "no-receive-specified");
            Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
            
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

        #region CommandAsync

        //TODO: waiting for PersistAsync
        //[Fact]
        //public void Given_a_persistent_actor_with_CommandAsync_When_sending_two_commands_first_one_with_async_persist_Then_second_one_will_be_called_after_persist_callback()
        //{
        //    //Given
        //    var pid = "p-7";

        //    //When

        //    //Then
        //}

        //TODO: waiting for PersistAsync
        //[Fact]
        //public void Given_a_persistent_actor_with_CommandAsync_When_sending_a_command_with_async_persist_Then_persist_will_be_called()
        //{
        //    //Given
        //    const string msg = "hello";
        //    var pid = "p-8";
        //    WriteEvents(pid, 1, 2, 3);
        //    var actor = Sys.ActorOf(Props.Create(() => new AsyncCommandActor(pid, 3000)), "asyncpersist");

        //    //When
        //    actor.Tell(new AsyncMsg(msg, shouldPersist: true));

        //    //Then
        //    ExpectMsg((object) "async:" + msg);
        //    ExpectMsg((object) "callback:" + msg);
        //}

        [Fact]
        public void Given_a_persistent_actor_with_Command_and_CommandAsync_When_sending_a_command_synchronously_and_another_asynchronously_Then_there_will_be_no_data_race()
        {
            //Given
            const string msg = "hello";
            var pid = "p-9";
            WriteEvents(pid, 1, 2, 3);
            var actor = Sys.ActorOf(Props.Create(() => new AsyncCommandActor(pid, 3000)), "asynccommand");

            //When
            actor.Tell(new AsyncMsg(msg));
            actor.Tell(new SyncMsg(msg));

            //Then
            ExpectMsg((object) "async:" + msg);
            ExpectMsg((object) "sync:" + msg);
        }
        #endregion

        private readonly AtomicCounterLong _seqNrCounter = new AtomicCounterLong(1L);
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
            public readonly LinkedList<object> State = new LinkedList<object>();
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
                Command<object>(m =>
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

        private class AsyncMsg
        {
            public string Msg { get; set; }
            public bool ShouldPersist { get; }

            public AsyncMsg(string msg, bool shouldPersist = false)
            {
                Msg = msg;
                ShouldPersist = shouldPersist;
            }
        }

        private class SyncMsg
        {
            public string Msg { get; }

            public SyncMsg(string msg) { Msg = msg; }
        }

        private class AsyncCommandActor : TestReceivePersistentActor
        {
            public AsyncCommandActor(string pid, int wait) : base(pid)
            {
               Command<SyncMsg>(sync => Sender.Tell("sync:" + sync.Msg, Self));
               CommandAsync<AsyncMsg>(async @async =>
               {
                   await Task.Delay(wait);
                   if (@async.ShouldPersist)
                   {
                        Sender.Tell("callback:"+ @async.Msg, Self);
                   }
                   Sender.Tell("async:" + @async.Msg, Self);
               });
            }
        }
    }
}

