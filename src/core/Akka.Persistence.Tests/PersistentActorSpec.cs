//-----------------------------------------------------------------------
// <copyright file="PersistentActorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.Tests
{
    public partial class PersistentActorSpec : PersistenceSpec
    {
        private readonly Random _random = new Random();
        public PersistentActorSpec()
            : base(PersistenceSpec.Configuration("inmem", "PersistentActorSpec"))
        {
            var pref = ActorOf(Props.Create(() => new BehaviorOneActor(Name)));
            pref.Tell(new Cmd("a"));
            pref.Tell(GetState.Instance);
            ExpectMsgInOrder("a-1", "a-2");
        }

        [Fact]
        public void PersistentActor_should_recover_from_persisted_events()
        {
            var pref = ActorOf(Props.Create(() => new BehaviorOneActor(Name)));
            pref.Tell(GetState.Instance);
            ExpectMsgInOrder("a-1", "a-2");
        }

        [Fact]
        public void PersistentActor_should_handle_multiple_emitted_events_in_correct_order_for_single_persist_call()
        {
            var pref = ActorOf(Props.Create(() => new BehaviorOneActor(Name)));
            pref.Tell(new Cmd("b"));
            pref.Tell(GetState.Instance);
            ExpectMsgInOrder("a-1", "a-2", "b-1", "b-2");
        }

        [Fact]
        public void PersistentActor_should_handle_multiple_emitted_events_in_correct_order_for_multiple_persist_calls()
        {
            var pref = ActorOf(Props.Create(() => new BehaviorTwoActor(Name)));
            pref.Tell(new Cmd("b"));
            pref.Tell(GetState.Instance);
            ExpectMsgInOrder("a-1", "a-2", "b-1", "b-2", "b-3", "b-4");
        }

        [Fact]
        public void PersistentActor_should_receive_emitted_events_immediately_after_command()
        {
            var pref = ActorOf(Props.Create(() => new BehaviorThreeActor(Name)));
            pref.Tell(new Cmd("b"));
            pref.Tell(new Cmd("c"));
            pref.Tell(GetState.Instance);
            ExpectMsgInOrder("a-1", "a-2", "b-10", "b-11", "b-12", "c-10", "c-11", "c-12");
        }

        [Fact]
        public void PersistentActor_should_recover_on_command_failure()
        {
            var pref = ActorOf(Props.Create(() => new BehaviorThreeActor(Name)));
            pref.Tell(new Cmd("b"));
            pref.Tell("boom");
            pref.Tell(new Cmd("c"));
            pref.Tell(GetState.Instance);
            // cmd that was added to state before failure (b-10) is not replayed ...
            ExpectMsgInOrder("a-1", "a-2", "b-11", "b-12", "c-10", "c-11", "c-12");
        }

        [Fact]
        public void PersistentActor_should_allow_behavior_changes_in_event_handler_when_handling_first_event()
        {
            var pref = ActorOf(Props.Create(() => new ChangeBehaviorInFirstEventHandlerActor(Name)));
            pref.Tell(new Cmd("b"));
            pref.Tell(new Cmd("c"));
            pref.Tell(new Cmd("d"));
            pref.Tell(new Cmd("e"));
            pref.Tell(GetState.Instance);
            ExpectMsgInOrder("a-1", "a-2", "b-0", "c-21", "c-22", "d-0", "e-21", "e-22");
        }

        [Fact]
        public void PersistentActor_should_allow_behavior_changes_in_event_handler_when_handling_the_last_event()
        {
            var pref = ActorOf(Props.Create(() => new ChangeBehaviorInLastEventHandlerActor(Name)));
            pref.Tell(new Cmd("b"));
            pref.Tell(new Cmd("c"));
            pref.Tell(new Cmd("d"));
            pref.Tell(new Cmd("e"));
            pref.Tell(GetState.Instance);
            ExpectMsgInOrder("a-1", "a-2", "b-0", "c-21", "c-22", "d-0", "e-21", "e-22");
        }

        [Fact]
        public void PersistentActor_should_allow_behavior_changes_in_event_handler_as_first_action()
        {
            var pref = ActorOf(Props.Create(() => new ChangeBehaviorInCommandHandlerFirstActor(Name)));
            pref.Tell(new Cmd("b"));
            pref.Tell(new Cmd("c"));
            pref.Tell(new Cmd("d"));
            pref.Tell(new Cmd("e"));
            pref.Tell(GetState.Instance);
            ExpectMsgInOrder("a-1", "a-2", "b-0", "c-30", "c-31", "c-32", "d-0", "e-30", "e-31", "e-32");
        }

        [Fact]
        public void PersistentActor_should_allow_behavior_changes_in_event_handler_as_last_action()
        {
            var pref = ActorOf(Props.Create(() => new ChangeBehaviorInCommandHandlerLastActor(Name)));
            pref.Tell(new Cmd("b"));
            pref.Tell(new Cmd("c"));
            pref.Tell(new Cmd("d"));
            pref.Tell(new Cmd("e"));
            pref.Tell(GetState.Instance);
            ExpectMsgInOrder("a-1", "a-2", "b-0", "c-30", "c-31", "c-32", "d-0", "e-30", "e-31", "e-32");
        }

        [Fact]
        public void PersistentActor_should_support_snapshotting()
        {
            var pref = ActorOf(Props.Create(() => new SnapshottingPersistentActor(Name, TestActor)));
            pref.Tell(new Cmd("b"));
            pref.Tell("snap");
            pref.Tell(new Cmd("c"));
            ExpectMsg("saved");
            pref.Tell(GetState.Instance);
            ExpectMsgInOrder("a-1", "a-2", "b-41", "b-42", "c-41", "c-42");

            var pref2 = ActorOf(Props.Create(() => new SnapshottingPersistentActor(Name, TestActor)));
            ExpectMsg("offered");
            pref2.Tell(GetState.Instance);
            ExpectMsgInOrder("a-1", "a-2", "b-41", "b-42", "c-41", "c-42");
        }

        [Fact]
        public void PersistentActor_should_support_Context_Become_during_recovery()
        {
            var pref = ActorOf(Props.Create(() => new SnapshottingPersistentActor(Name, TestActor)));
            pref.Tell(new Cmd("b"));
            pref.Tell("snap");
            pref.Tell(new Cmd("c"));
            ExpectMsg("saved");
            pref.Tell(GetState.Instance);
            ExpectMsgInOrder("a-1", "a-2", "b-41", "b-42", "c-41", "c-42");

            var pref2 = ActorOf(Props.Create(() => new SnapshottingBecomingPersistentActor(Name, TestActor)));
            ExpectMsg("offered");
            ExpectMsg("I'm becoming");
            pref2.Tell(GetState.Instance);
            ExpectMsgInOrder("a-1", "a-2", "b-41", "b-42", "c-41", "c-42");
        }

        [Fact]
        public void PersistentActor_should_be_able_to_reply_within_an_event_handler()
        {
            var pref = ActorOf(Props.Create(() => new ReplyInEventHandlerActor(Name)));
            pref.Tell(new Cmd("a"));
            ExpectMsg("a");
        }

        [Fact]
        public void PersistentActor_should_support_user_stash_operations()
        {
            var pref = ActorOf(Props.Create(() => new UserStashActor(Name)));
            pref.Tell(new Cmd("a"));
            pref.Tell(new Cmd("b"));
            pref.Tell(new Cmd("c"));
            ExpectMsg("b");
            ExpectMsg("c");
            ExpectMsg("a");
        }

        [Fact]
        public void PersistentActor_should_support_user_stash_operations_with_several_stashed_messages()
        {
            var pref = ActorOf(Props.Create(() => new UserStashManyActor(Name)));
            var n = 10;
            var commands = Enumerable.Range(1, n).SelectMany(_ => new[] { new Cmd("a"), new Cmd("b-1"), new Cmd("b-2"), new Cmd("c"), });

            foreach (var command in commands)
            {
                pref.Tell(command);
            }

            pref.Tell(GetState.Instance);

            var events = new List<object> { "a-1", "a-2" };
            for (int i = 0; i < n; i++)
            {
                events.AddRange(new[] { "a", "c", "b-1", "b-2" });
            }

            ExpectMsgInOrder(events.ToArray());
        }

        [Fact]
        public void PersistentActor_should_preserve_order_of_incoming_messages()
        {
            var pref = ActorOf(Props.Create(() => new StressOrdering(Name)));
            var latch = CreateTestLatch(1);

            pref.Tell(new Cmd("a"));
            pref.Tell(new LatchCmd(latch, "b"));    // for some reason after this line of code test hangs
            pref.Tell("c");

            ExpectMsg("a");
            ExpectMsg("b");
            pref.Tell("d");
            latch.CountDown();
            ExpectMsg("c");
            ExpectMsg("d");
        } 

        [Fact]
        public void PersistentActor_should_support_user_stash_operations_under_failures()
        {
            var pref = ActorOf(Props.Create(() => new UserStashFailureActor(Name)));
            pref.Tell(new Cmd("a"));
            for (int i = 1; i <= 10; i++)
            {
                var cmd = new Cmd("b-" + i);
                pref.Tell(cmd);
            }
            pref.Tell(new Cmd("c"));
            pref.Tell(GetState.Instance);
            ExpectMsgInOrder("a-1", "a-2", "a", "c", "b-1", "b-3", "b-4", "b-5", "b-6", "b-7", "b-8", "b-9", "b-10");
        }

        [Fact]
        public void PersistentActor_should_be_able_to_persist_value_types_as_events()
        {
            var pref = ActorOf(Props.Create(() => new IntEventPersistentActor(Name)));
            pref.Tell(new Cmd("a"));
            ExpectMsg(5L);
        }

        [Fact]
        public void PersistentActor_should_be_able_to_opt_out_from_stashing_messages_until_all_events_has_been_processed()
        {
            var pref = ActorOf(Props.Create(() => new AsyncPersistActor(Name)));
            pref.Tell(new Cmd("x"));
            pref.Tell(new Cmd("y"));
            ExpectMsg("x");
            ExpectMsg("y");
            ExpectMsg("x-1");
            ExpectMsg("y-2");
        }

        [Fact]
        public void PersistentActor_should_support_mutli_PersistAsync_calls_for_one_command_and_executed_them_when_possible()
        {
            var pref = ActorOf(Props.Create(() => new AsyncPersistThreeTimesActor(Name)));
            var commands = Enumerable.Range(1, 10).Select(i => new Cmd("c-" + i)).ToArray();

            foreach (var command in commands)
            {
                Thread.Sleep(_random.Next(10));
                pref.Tell(command);
            }

            // each command = 1 reply + 3 event-replies
            var all = ReceiveN(40).Select(x => x.ToString()).ToArray();
            var replies = all.Where(r => r.Count(c => c == '-') == 1);
            replies.ShouldOnlyContainInOrder(commands.Select(cmd => cmd.Data).ToArray());

            // range(3, 30) is equivalent of Scala (3 to 32)
            var expectedAcks = Enumerable.Range(3, 30).Select(i => "a-" + (i / 3) + "-" + (i - 2)).ToArray();
            var acks = all.Where(r => r.Count(c => c == '-') == 2);
            acks.ShouldOnlyContainInOrder(expectedAcks);
        }

        [Fact]
        public void PersistentActor_should_reply_to_the_original_sender_of_a_command_even_on_PersistAsync()
        {
            // sanity check, the setting of Sender for PersistentRepresentation is handled by PersistentActor currently
            // but as we want to remove it soon, keeping the explicit test here.
            var pref = ActorOf(Props.Create(() => new AsyncPersistThreeTimesActor(Name)));
            var commands = Enumerable.Range(1, 10).Select(i => new Cmd("c-" + i)).ToArray();
            var probes = Enumerable.Range(1, 10).Select(_ => CreateTestProbe()).ToArray();

            for (int i = 0; i < 10; i++)
            {
                pref.Tell(commands[i], probes[i].Ref);
            }

            Within(TimeSpan.FromSeconds(3), () =>
            {
                foreach (var probe in probes)
                {
                    probe.ExpectMsgAllOf<string>();
                }
            });
        }

        [Fact]
        public void PersistentActor_should_support_the_same_event_being_PersistAsynced_multiple_times()
        {
            var pref = ActorOf(Props.Create(() => new AsyncPersistSameEventTwiceActor(Name)));
            pref.Tell(new Cmd("x"));
            ExpectMsg("x");

            ExpectMsg("x-a-1");
            ExpectMsg("x-b-2");
            ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void PersistentActor_should_support_a_mix_of_persist_calls_and_persist_calls_in_expected_order()
        {
            var pref = ActorOf(Props.Create(() => new AsyncPersistAndPersistMixedSyncAsyncSyncActor(Name)));
            pref.Tell(new Cmd("a"));
            pref.Tell(new Cmd("b"));
            pref.Tell(new Cmd("c"));

            ExpectMsg("a");
            ExpectMsg("a-e1-1");    // persist
            ExpectMsg("a-ea2-2");   // persist async, but ordering enforced by sync persist below
            ExpectMsg("a-e3-3");    // persist

            ExpectMsg("b");
            ExpectMsg("b-e1-4");
            ExpectMsg("b-ea2-5");
            ExpectMsg("b-e3-6");

            ExpectMsg("c");
            ExpectMsg("c-e1-7");
            ExpectMsg("c-ea2-8");
            ExpectMsg("c-e3-9");

            ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void PersistentActor_should_support_a_mix_of_persist_calls_and_persist_async_calls()
        {
            var pref = ActorOf(Props.Create(() => new AsyncPersistAndPersistMixedSyncAsyncActor(Name)));
            pref.Tell(new Cmd("a"));
            pref.Tell(new Cmd("b"));
            pref.Tell(new Cmd("c"));

            ExpectMsg("a");
            ExpectMsg("a-e1-1");    // persist, must be before next command

            var expected = new HashSet<string> { "b", "a-ea2-2" };
            var found = ExpectMsgAnyOf(expected.Cast<object>().ToArray());  // ea2 is PersistAsyn, b can be processed before it
            expected.Remove(found.ToString());
            ExpectMsgAnyOf(expected.Cast<object>().ToArray());

            ExpectMsg("b-e1-3");        // persist, must be before next command

            var expected2 = new HashSet<string> { "c", "b-ea2-4" };
            var found2 = ExpectMsgAnyOf(expected2.Cast<object>().ToArray());
            expected.Remove(found2.ToString());
            ExpectMsgAnyOf(expected2.Cast<object>().ToArray());

            ExpectMsg("c-e1-5");
            ExpectMsg("c-ea2-6");

            ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void PersistentActor_should_correlate_PersistAsync_handlers_after_restart()
        {
            var pref = ActorOf(Props.Create(() => new AsyncPersistHandlerCorrelationCheck(Name)));
            for (int i = 1; i < 100; i++)
            {
                pref.Tell(new Cmd(i));
            }
            pref.Tell("boom");
            for (int i = 1; i < 20; i++)
            {
                pref.Tell(new Cmd(i));
            }
            pref.Tell(new Cmd("done"));
            ExpectMsg("done", TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void PersistentActor_should_allow_deferring_handlers_in_order_to_provide_ordered_processing_in_respect_to_Persist_handlers()
        {
            var pref = ActorOf(Props.Create(() => new DeferringWithPersistActor(Name)));
            pref.Tell(new Cmd("a"));

            ExpectMsg("d-1");
            ExpectMsg("a-2");
            ExpectMsg("d-3");
            ExpectMsg("d-4");

            ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void PersistentActor_should_allow_deferring_handlers_in_order_to_provide_ordered_processing_in_respect_to_PersistAsync_handlers()
        {
            var pref = ActorOf(Props.Create(() => new DeferringWithAsyncPersistActor(Name)));
            pref.Tell(new Cmd("a"));

            ExpectMsg("d-a-1");
            ExpectMsg("pa-a-2");
            ExpectMsg("d-a-3");
            ExpectMsg("d-a-4");

            ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void PersistentActor_should_invoke_deferred_handlers_in_presence_of_mixed_a_long_series_Persist_and_PersistAsync_calls()
        {
            var pref = ActorOf(Props.Create(() => new DeferringMixedCallsPPADDPADPersistActor(Name)));
            var p1 = CreateTestProbe();
            var p2 = CreateTestProbe();

            pref.Tell(new Cmd("a"), p1.Ref);
            pref.Tell(new Cmd("b"), p2.Ref);
            p1.ExpectMsg("p-a-1");
            p1.ExpectMsg("pa-a-2");
            p1.ExpectMsg("d-a-3");
            p1.ExpectMsg("d-a-4");
            p1.ExpectMsg("pa-a-5");
            p1.ExpectMsg("d-a-6");

            p2.ExpectMsg("p-b-1");
            p2.ExpectMsg("pa-b-2");
            p2.ExpectMsg("d-b-3");
            p2.ExpectMsg("d-b-4");
            p2.ExpectMsg("pa-b-5");
            p2.ExpectMsg("d-b-6");

            ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void PersistentActor_should_invoke_deferred_handlers_right_away_if_there_are_no_persist_handlers_registered()
        {
            var pref = ActorOf(Props.Create(() => new DeferringWithNoPersistCallsPersistActor(Name)));
            pref.Tell(new Cmd("a"));

            ExpectMsg("d-1");
            ExpectMsg("d-2");
            ExpectMsg("d-3");

            ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void PersistentActor_should_invoke_deferred_handlers_preserving_the_original_sender_reference()
        {
            var pref = ActorOf(Props.Create(() => new DeferringWithAsyncPersistActor(Name)));
            var p1 = CreateTestProbe();
            var p2 = CreateTestProbe();

            pref.Tell(new Cmd("a"), p1.Ref);
            pref.Tell(new Cmd("b"), p2.Ref);

            p1.ExpectMsg("d-a-1");
            p1.ExpectMsg("pa-a-2");
            p1.ExpectMsg("d-a-3");
            p1.ExpectMsg("d-a-4");

            p2.ExpectMsg("d-b-1");
            p2.ExpectMsg("pa-b-2");
            p2.ExpectMsg("d-b-3");
            p2.ExpectMsg("d-b-4");

            ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void PersistentActor_should_receiver_RecoveryFinished_if_its_handled_after_all_events_have_been_replayed()
        {
            var pref = ActorOf(Props.Create(() => new SnapshottingPersistentActor(Name, TestActor)));
            pref.Tell(new Cmd("b"));
            pref.Tell("snap");
            pref.Tell(new Cmd("c"));
            ExpectMsg("saved");
            pref.Tell(GetState.Instance);
            ExpectMsgInOrder("a-1", "a-2", "b-41", "b-42", "c-41", "c-42");

            var pref2 = ActorOf(Props.Create(() => new HandleRecoveryFinishedEventPersistentActor(Name, TestActor)));
            ExpectMsg("offered");
            ExpectMsg<RecoveryCompleted>();
            ExpectMsg("I am the stashed");
            ExpectMsg("I am the recovered");
            pref2.Tell(GetState.Instance);
            ExpectMsgInOrder("a-1", "a-2", "b-41", "b-42", "c-41", "c-42", RecoveryCompleted.Instance);
        }
    }
}

