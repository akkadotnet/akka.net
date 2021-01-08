//-----------------------------------------------------------------------
// <copyright file="ActorWithStashSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Tests.TestUtils;
using Xunit;

namespace Akka.Tests.Actor.Stash
{
    public class ActorWithStashSpec : AkkaSpec
    {
        private static StateObj _state;
        public ActorWithStashSpec()
        {
            _state = new StateObj(this);
        }

        [Fact]
        public void An_actor_with_unbounded_stash_must_have_a_stash_assigned_to_it()
        {
            var actor = ActorOfAsTestActorRef<UnboundedStashActor>();
            Assert.NotNull(actor.UnderlyingActor.Stash);
            Assert.IsAssignableFrom<UnboundedStashImpl>(actor.UnderlyingActor.Stash);
        }

        [Fact]
        public void An_actor_with_bounded_stash_must_have_a_stash_assigned_to_it()
        {
            var actor = ActorOfAsTestActorRef<BoundedStashActor>();
            Assert.NotNull(actor.UnderlyingActor.Stash);
            Assert.IsAssignableFrom<BoundedStashImpl>(actor.UnderlyingActor.Stash);
        }

        [Fact]
        public void An_actor_with_Stash_Must_stash_and_unstash_messages()
        {
            var stasher = ActorOf<StashingActor>("stashing-actor");
            stasher.Tell("bye");    //will be stashed
            stasher.Tell("hello");  //forces UnstashAll
            _state.Finished.Await();
            _state.S.ShouldBe("bye");
        }

        [Fact]
        public void An_actor_Must_throw_an_exception_if_the_same_message_is_stashed_twice()
        {
            _state.ExpectedException = new TestLatch();
            var stasher = ActorOf<StashingTwiceActor>("stashing-actor");
            stasher.Tell("hello");
            _state.ExpectedException.Ready(TimeSpan.FromSeconds(3));
        }

        [Fact]
        public void An_actor_Should__not_throw_an_exception_if_the_same_message_is_received_and_stashed_twice()
        {
            _state.ExpectedException = new TestLatch();
            var stasher = ActorOf<StashAndReplyActor>("stashing-actor");
            stasher.Tell("hello");
            ExpectMsg("bye");
            stasher.Tell("hello");
            ExpectMsg("bye");
        }

        [Fact]
        public void An_actor_must_unstash_all_messages_on_PostStop()
        {
            var stasher = ActorOf<StashEverythingActor>("stasher");
            Watch(stasher);
            //This message will be stashed by stasher
            stasher.Tell("message");

            //When stasher is stopped it should unstash message during poststop to mailbox
            //the mailbox will be emptied and the messages will be sent to deadletters
            EventFilter.DeadLetter<string>(s=>s=="message", source: stasher.Path.ToString()).ExpectOne(() =>
            {
                Sys.Stop(stasher);
                ExpectTerminated(stasher);
            });
        }

        [Fact]
        public void An_actor_Must_process_stashed_messages_after_restart()
        {
            SupervisorStrategy strategy = new OneForOneStrategy(2, TimeSpan.FromSeconds(1), e => Directive.Restart);
            var boss = ActorOf(() => new Supervisor(strategy));
            var restartLatch = CreateTestLatch();
            var hasMsgLatch = CreateTestLatch();
            var slaveProps = Props.Create(() => new SlaveActor(restartLatch, hasMsgLatch, "stashme"));

            //Send the props to supervisor, which will create an actor and return the ActorRef
            var slave = boss.AskAndWait<IActorRef>(slaveProps, TestKitSettings.DefaultTimeout);

            //send a message that will be stashed
            slave.Tell("stashme");

            //this will crash the slave.
            slave.Tell("crash");

            //During preRestart restartLatch is opened
            //After that the cell will unstash "stashme", it should be received by the slave and open hasMsgLatch
            restartLatch.Ready(TimeSpan.FromSeconds(10));
            hasMsgLatch.Ready(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public void An_actor_that_clears_the_stash_on_preRestart_Must_not_receive_previously_stashed_messages()
        {
            SupervisorStrategy strategy = new OneForOneStrategy(2, TimeSpan.FromSeconds(1), e => Directive.Restart);
            var boss = ActorOf(() => new Supervisor(strategy));
            var restartLatch = CreateTestLatch();
            var slaveProps = Props.Create(() => new ActorsThatClearsStashOnPreRestart(restartLatch));

            //Send the props to supervisor, which will create an actor and return the ActorRef
            var slave = boss.AskAndWait<IActorRef>(slaveProps, TestKitSettings.DefaultTimeout);

            //send messages that will be stashed
            slave.Tell("stashme 1");
            slave.Tell("stashme 2");
            slave.Tell("stashme 3");

            //this will crash the slave.
            slave.Tell("crash");

            //send a message that should not be stashed
            slave.Tell("this should bounce back");

            //During preRestart restartLatch is opened
            //After that the cell will clear the stash
            //So when the cell tries to unstash, it will not unstash messages. If it would TestActor
            //would receive all stashme messages instead of "this should bounce back"
            restartLatch.Ready(TimeSpan.FromSeconds(1110));
            ExpectMsg("this should bounce back");
        }

        [Fact]
        public void An_actor_must_rereceive_unstashed_Terminated_messages()
        {
            ActorOf(Props.Create(() => new TerminatedMessageStashingActor(TestActor)), "terminated-message-stashing-actor");
            ExpectMsg("terminated1");
            ExpectMsg("terminated2");
        }

        private class UnboundedStashActor : BlackHoleActor, IWithUnboundedStash
        {
            public IStash Stash { get; set; }
        }

        private class BoundedStashActor : BlackHoleActor, IWithBoundedStash
        {
            public IStash Stash { get; set; }
        }

        private class StashingActor : TestReceiveActor, IWithUnboundedStash
        {
            public StashingActor()
            {
                Receive("hello", m =>
                {
                    _state.S = "hello";
                    Stash.UnstashAll();
                    Become(Greeted);
                });
                ReceiveAny(m => Stash.Stash());
            }

            private void Greeted()
            {
                Receive("bye", _ =>
                {
                    _state.S = "bye";
                    _state.Finished.Await();
                });
                ReceiveAny(_ => { }); //Do nothing
            }

            public IStash Stash { get; set; }
        }

        private class StashAndReplyActor : ReceiveActor, IWithUnboundedStash
        {
            public StashAndReplyActor()
            {
                ReceiveAny(m =>
                {
                    Stash.Stash();
                    Sender.Tell("bye");
                }
                );
            }
            public IStash Stash { get; set; }
        }

        private class StashEverythingActor : ReceiveActor, IWithUnboundedStash
        {
            public StashEverythingActor()
            {
                ReceiveAny(m=>Stash.Stash());
            }
            public IStash Stash { get; set; }
        }

        private class StashingTwiceActor : TestReceiveActor, IWithUnboundedStash
        {
            public StashingTwiceActor()
            {
                Receive("hello", m =>
                {
                    Stash.Stash();
                    try
                    {
                        Stash.Stash();
                    }
                    catch(IllegalActorStateException)
                    {
                        _state.ExpectedException.Open();
                    }
                });
                ReceiveAny(m => { });
            }

            private void Greeted()
            {
                Receive("bye", _ =>
                {
                    _state.S = "bye";
                    _state.Finished.Await();
                });
                ReceiveAny(_ => { }); //Do nothing
            }

            public IStash Stash { get; set; }
        }

        private class SlaveActor : TestReceiveActor, IWithUnboundedStash
        {
            private readonly TestLatch _restartLatch;

            public SlaveActor(TestLatch restartLatch, TestLatch hasMsgLatch, string expectedUnstashedMessage)
            {
                _restartLatch = restartLatch;
                Receive("crash", _ => { throw new Exception("Received \"crash\""); });

                // when restartLatch is not yet open, stash all messages != "crash"                
                Receive<object>(_ => !restartLatch.IsOpen, m => Stash.Stash());

                // when restartLatch is open, must receive the unstashed message
                Receive(expectedUnstashedMessage, _ => hasMsgLatch.Open());
            }

            protected override void PreRestart(Exception reason, object message)
            {
                if(!_restartLatch.IsOpen) _restartLatch.Open();

                base.PreRestart(reason, message);
            }
            public IStash Stash { get; set; }
        }

        private class ActorsThatClearsStashOnPreRestart : TestReceiveActor, IWithUnboundedStash
        {
            private readonly TestLatch _restartLatch;

            public ActorsThatClearsStashOnPreRestart(TestLatch restartLatch)
            {
                _restartLatch = restartLatch;
                Receive("crash", _ => { throw new Exception("Received \"crash\""); });

                // when restartLatch is not yet open, stash all messages != "crash"                
                Receive<object>(_ => !restartLatch.IsOpen, m => Stash.Stash());

                // when restartLatch is open we send all messages back
                ReceiveAny(m => Sender.Tell(m));
            }
            protected override void PreRestart(Exception reason, object message)
            {
                if(!_restartLatch.IsOpen) _restartLatch.Open();
                Stash.ClearStash();
                base.PreRestart(reason, message);
            }
            public IStash Stash { get; set; }

        }

        private class TerminatedMessageStashingActor : TestReceiveActor, IWithUnboundedStash
        {
            public TerminatedMessageStashingActor(IActorRef probe)
            {
                var watchedActor=Context.Watch(Context.ActorOf<BlackHoleActor>("watched-actor"));
                var stashed = false;
                Context.Stop(watchedActor);

                Receive<Terminated>(w => w.ActorRef == watchedActor, w =>
                {
                    if(!stashed)
                    {
                        Stash.Stash();        //Stash the Terminated message
                        stashed = true;
                        Stash.UnstashAll();   //Unstash the Terminated message
                        probe.Tell("terminated1");
                    }
                    else
                    {
                        probe.Tell("terminated2");
                    }
                });
            }
            public IStash Stash { get; set; }

        }

        private class StateObj
        {
            public StateObj(TestKitBase testKit)
            {
                S = "";
                Finished = testKit.CreateTestBarrier(2);
            }
            public string S;
            public TestBarrier Finished;
            public TestLatch ExpectedException;
        }

        [Fact]
        public void An_actor_should_not_throw_an_exception_if_sent_two_messages_with_same_value_different_reference()
        {
            _state.ExpectedException = new TestLatch();
            var stasher = ActorOf<StashEverythingActor>("stashing-actor");
            stasher.Tell(new CustomMessageOverrideEquals("A"));
            stasher.Tell(new CustomMessageOverrideEquals("A"));

            // NOTE:
            // here we should test for no exception thrown..
            // but I don't know how....
        }

        public class CustomMessageOverrideEquals
        {

            public CustomMessageOverrideEquals(string cargo)
            {
                Cargo = cargo;
            }
            public override int GetHashCode()
            {
                return base.GetHashCode() ^ 314;
            }
            public override bool Equals(System.Object obj)
            {
                // If parameter is null return false.
                if (obj == null)
                {
                    return false;
                }

                // If parameter cannot be cast to Point return false.
                CustomMessageOverrideEquals p = obj as CustomMessageOverrideEquals;
                if ((System.Object)p == null)
                {
                    return false;
                }

                // Return true if the fields match:
                return (Cargo == p.Cargo);
            }

            public bool Equals(CustomMessageOverrideEquals p)
            {
                // If parameter is null return false:
                if ((object)p == null)
                {
                    return false;
                }

                // Return true if the fields match:
                return (Cargo == p.Cargo);
            }
            public static bool operator ==(CustomMessageOverrideEquals a, CustomMessageOverrideEquals b)
            {
                // If both are null, or both are same instance, return true.
                if (System.Object.ReferenceEquals(a, b))
                {
                    return true;
                }

                // If one is null, but not both, return false.
                if (((object)a == null) || ((object)b == null))
                {
                    return false;
                }

                // Return true if the fields match:
                return (a.Cargo == b.Cargo);
            }

            public static bool operator !=(CustomMessageOverrideEquals a, CustomMessageOverrideEquals b)
            {
                return !(a == b);
            }
            public string Cargo { get; private set; }
        }
    }
}

