using System;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Tests.TestUtils;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    public class DeathWatchSpec : AkkaSpec
    {
        private readonly InternalActorRef _supervisor;
        private ActorRef _terminal;

        public DeathWatchSpec()
        {
            _supervisor = System.ActorOf(Props.Create(() => new Supervisor(SupervisorStrategy.DefaultStrategy)), "watchers");
            _terminal = sys.ActorOf(Props.Empty);
        }

        [Fact]
        public void DeathWatch_must_notify_with_one_Terminated_message_when_an_Actor_is_already_terminated()
        {
            var terminal = System.ActorOf(Props.Empty, "killed-actor");
            terminal.Tell(PoisonPill.Instance, testActor);
            StartWatching(terminal);
            ExpectTerminationOf(terminal);
        }

        //        protected override string GetConfig()
        //        {
        //            return @"
        //                akka.log-dead-letters-during-shutdown = true
        //                akka.actor.debug.autoreceive = true
        //                akka.actor.debug.lifecycle = true
        //                akka.actor.debug.event-stream = true
        //                akka.actor.debug.unhandled = true
        //                akka.log-dead-letters = true
        //                akka.loglevel = DEBUG
        //                akka.stdout-loglevel = DEBUG
        //            ";
        //        }
        [Fact]
        public void Bug209_any_user_messages_following_a_Terminate_message_should_be_forwarded_to_DeadLetterMailbox()
        {
            var actor = (LocalActorRef)System.ActorOf(Props.Empty, "killed-actor");

            sys.EventStream.Subscribe(testActor, typeof(DeadLetter));

            var mailbox = actor.Cell.Mailbox;
            //Wait for the mailbox to become idle after processed all initial messages.
            AwaitCond(() =>
                !mailbox.HasUnscheduledMessages && mailbox.Status == Mailbox.MailboxStatus.Idle,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(50));

            //Suspend the mailbox and post Terminate and a user message
            mailbox.Suspend();
            mailbox.Post(new Envelope() { Message = Terminate.Instance, Sender = testActor });
            mailbox.Post(new Envelope() { Message = "SomeUserMessage", Sender = testActor });

            //Resume the mailbox, which will also schedule
            mailbox.Resume();

            //The actor should Terminate, exchange the mailbox to a DeadLetterMailbox and forward the user message to the DeadLetterMailbox
            ExpectMsgPF<DeadLetter>(TimeSpan.FromSeconds(1), d => (string)d.Message == "SomeUserMessage");
            actor.Cell.Mailbox.ShouldBe(System.Mailboxes.DeadLetterMailbox);
        }

        [Fact]
        public void DeathWatch_must_notify_with_one_Terminated_message_when_actor_is_stopped()
        {
            const string msg = "hello";
            StartWatching(_terminal).Tell(msg);
            expectMsg(msg);
            _terminal.Tell(PoisonPill.Instance);
            ExpectTerminationOf(_terminal);
        }

        [Fact]
        public void DeathWatch_must_notify_with_all_monitors_with_one_Terminated_message_when_Actor_is_stopped()
        {
            var monitor1 = StartWatching(_terminal);
            var monitor2 = StartWatching(_terminal);
            var monitor3 = StartWatching(_terminal);

            _terminal.Tell(PoisonPill.Instance);

            ExpectTerminationOf(_terminal);
            ExpectTerminationOf(_terminal);
            ExpectTerminationOf(_terminal);

            sys.Stop(monitor1);
            sys.Stop(monitor2);
            sys.Stop(monitor3);
        }

        [Fact]
        public void DeathWatch_must_notify_with_current_monitors_with_one_Terminated_message_when_Actor_is_stopped()
        {
            var monitor1 = StartWatching(_terminal);
            var monitor2 = sys.ActorOf(Props.Create(() => new WatchAndUnwatchMonitor(_terminal, testActor)).WithDeploy(Deploy.Local));
            var monitor3 = StartWatching(_terminal);

            monitor2.Tell("ping");
            expectMsg("pong");      // since Watch and Unwatch are asynchronous, we need some sync

            _terminal.Tell(PoisonPill.Instance);

            ExpectTerminationOf(_terminal);
            ExpectTerminationOf(_terminal);

            sys.Stop(monitor1);
            sys.Stop(monitor2);
            sys.Stop(monitor3);
        }

        //[Fact]
        //public void DeathWatch_must_notify_with_a_Terminated_message_once_when_Actor_is_stopped_but_not_when_restarted()
        //{
        //    EventFilter<ActorKilledException>(null, 1, () =>
        //    {
        //        var supervisor = sys.ActorOf(Props.Create(() => new Supervisor(
        //            new OneForOneStrategy(2, TimeSpan.FromSeconds(1), SupervisorStrategy.DefaultDecider))));    // DefaultDecider will cause Restart directive

        //        var terminal = AwaitResult<InternalActorRef>(supervisor.Ask(Props.Create(() => new EchoTestActor())), DefaultTimeout);
        //        var monitor = AwaitResult<ActorRef>(supervisor.Ask(CreateWatchAndForwarderProps(terminal, testActor), DefaultTimeout), DefaultTimeout);

        //        terminal.Tell(Kill.Instance);
        //        terminal.Tell(Kill.Instance);

        //        var foo = AwaitResult<string>(terminal.Ask("foo", DefaultTimeout), DefaultTimeout);
        //        foo.ShouldBe("foo");

        //        terminal.Tell(Kill.Instance);

        //        ExpectTerminationOf(terminal);
        //        terminal.IsTerminated.ShouldBe(true);

        //        sys.Stop(supervisor);
        //    });
        //}

        //// See issue: #61
        //[Fact]
        //public void DeathWatch_must_fail_a_monitor_which_doesnt_handle_Terminated()
        //{
        //    FilterEvents(sys, new EventFilter[] { new ErrorFilter<ActorKilledException>(), new ErrorFilter<DeathPactException>() }, () =>
        //    {
        //        // Strategy has to be a custom derivative of OneForOneStrategy which implements custom ProcessFailure method in following manner:
        //        //
        //        //  override def handleFailure(context: ActorContext, child: ActorRef, cause: Throwable, stats: ChildRestartStats, children: Iterable[ChildRestartStats]) = {
        //        //      testActor.tell(FF(Failed(child, cause, 0)), child)
        //        //      super.handleFailure(context, child, cause, stats, children)
        //        //  }
        //        var strategy = new OneForOneStrategy(SupervisorStrategy.DefaultDecider);
        //        var supervisior = sys.ActorOf(Props.Create(() => new Supervisor(strategy)).WithDeploy(Deploy.Local));

        //        var failed = AwaitResult<ActorRef>(supervisior.Ask(Props.Empty), DefaultTimeout);
        //        var brother = AwaitResult<ActorRef>(supervisior.Ask(Props.Create(() => new BrotherActor(failed))), DefaultTimeout);

        //        StartWatching(brother);

        //        failed.Tell(Kill.Instance);
        //        var result = receiveWhile(TimeSpan.FromSeconds(3), msg =>
        //        {
        //            var res = 0;
        //            msg.Match()
        //                .With<FF>(ff =>
        //                {
        //                    if (ff.Fail.Cause is ActorKilledException && ff.Fail.Child == failed) res = 1;
        //                    if (ff.Fail.Cause is DeathPactException && ff.Fail.Child == brother) res = 2;
        //                })
        //                .With<WrappedTerminated>(x => res = x.Terminated.ActorRef == brother ? 3 : 0);
        //            return res;
        //        }, 3);

        //        ((InternalActorRef)testActor).IsTerminated.ShouldBe(false);
        //        result[0].ShouldBe(1);
        //        result[1].ShouldBe(2);
        //        result[2].ShouldBe(3);
        //    });
        //}

        [Fact]
        public void DeathWatch_must_be_able_to_watch_child_with_the_same_name_after_the_old_one_died()
        {
            var parent = sys.ActorOf(Props.Create(() => new KnobActor(testActor)).WithDeploy(Deploy.Local));

            parent.Tell(Knob);
            expectMsg(Bonk);
            parent.Tell(Knob);
            expectMsg(Bonk);
        }

        [Fact]
        public void DeathWatch_must_notify_only_when_watching()
        {
            var subject = sys.ActorOf(Props.Create(() => new EchoActor(_terminal)));
            testActor.Tell(new DeathWatchNotification(subject, true, false));
            expectNoMsg(TimeSpan.FromSeconds(3));
        }

        //// See issue: #61
        //[Fact]
        //public void DeathWatch_must_discard_Terminated_when_unwatched_between_sysmsg_and_processing()
        //{
        //    var t1 = new TestLatch(sys, 1);
        //    var t2 = new TestLatch(sys, 1);
        //    var p = new TestProbe();
        //    var w = sys.ActorOf(Props.Create(() => new Watcher()).WithDeploy(Deploy.Local), "testWatcher");

        //    w.Tell(new W(p.Ref));
        //    w.Tell(new Latches(t1, t2));
        //    t1.Ready(TimeSpan.FromSeconds(3));
        //    watch(testActor);
        //    p.Tell(Stop.Instance, testActor);
        //    p.ExpectTerminated(DefaultTimeout);
        //    w.Tell(new U(p.Ref));
        //    t2.CountDown();

        //    w.Tell(new Identify(null));
        //    expectMsg(new ActorIdentity(null, w), DefaultTimeout);
        //    w.Tell(new Identify(null));
        //    expectMsg(new ActorIdentity(null, w), DefaultTimeout);
        //}

        private void ExpectTerminationOf(ActorRef actorRef)
        {
            ExpectMsgPF<WrappedTerminated>(TimeSpan.FromSeconds(5), w => ReferenceEquals(w.Terminated.ActorRef, actorRef));
        }

        private ActorRef StartWatching(ActorRef target)
        {
            var task = _supervisor.Ask(CreateWatchAndForwarderProps(target, testActor), TimeSpan.FromSeconds(3));
            task.Wait(TimeSpan.FromSeconds(3));
            return (ActorRef)task.Result;
        }

        private Props CreateWatchAndForwarderProps(ActorRef target, ActorRef forwardToActor)
        {
            return Props.Create(() => new WatchAndForwardActor(target, forwardToActor));
        }

        internal class BrotherActor : ActorBase
        {
            public BrotherActor(ActorRef failed)
            {
                Context.Watch(failed);
            }

            protected override bool Receive(object message)
            {
                return true;
            }
        }

        internal const string Knob = "KNOB";
        internal const string Bonk = "BONK";
        internal class KnobKidActor : ActorBase
        {
            protected override bool Receive(object message)
            {
                message.Match().With<string>(x =>
                {
                    if (x == Knob)
                    {
                        Context.Stop(Self);
                    }
                });
                return true;
            }
        }
        internal class KnobActor : ActorBase
        {
            private readonly ActorRef _testActor;

            public KnobActor(ActorRef testActor)
            {
                _testActor = testActor;
            }

            protected override bool Receive(object message)
            {
                message.Match().With<string>(x =>
                {
                    if (x == Knob)
                    {
                        var kid = Context.ActorOf(Props.Create(() => new KnobKidActor()), "kid");
                        Context.Watch(kid);
                        kid.Forward(Knob);
                        Context.Become(msg =>
                        {
                            msg.Match().With<Terminated>(y =>
                            {
                                if (y.ActorRef == kid)
                                {
                                    _testActor.Tell(Bonk);
                                    Context.Unbecome();
                                }
                            });
                            return true;
                        });
                    }
                });
                return true;
            }
        }

        internal class WatchAndUnwatchMonitor : ActorBase
        {
            private readonly ActorRef _testActor;

            public WatchAndUnwatchMonitor(ActorRef terminal, ActorRef testActor)
            {
                _testActor = testActor;
                Context.Watch(terminal);
                Context.Unwatch(terminal);
            }

            protected override bool Receive(object message)
            {
                message.Match()
                    .With<string>(x =>
                    {
                        if (x == "ping")
                        {
                            _testActor.Tell("pong");
                        }
                    })
                    .With<Terminated>(x => _testActor.Tell(new WrappedTerminated(x)));
                return true;
            }
        }

        internal class Watcher : ActorBase
        {
            protected override bool Receive(object message)
            {
                message.Match()
                    .With<W>(w => Context.Watch(w.Ref))
                    .With<W>(w => Context.Unwatch(w.Ref))
                    .With<Latches>(x =>
                    {
                        x.T1.CountDown();
                        x.T2.Ready(TimeSpan.FromSeconds(3));
                    });
                return true;
            }
        }

        internal class WatchAndForwardActor : ActorBase
        {
            private readonly ActorRef _forwardToActor;

            public WatchAndForwardActor(ActorRef watchedActor, ActorRef forwardToActor)
            {
                _forwardToActor = forwardToActor;
                Context.Watch(watchedActor);
            }

            protected override bool Receive(object message)
            {
                var terminated = message as Terminated;
                if (terminated != null)
                    _forwardToActor.Forward(new WrappedTerminated(terminated));
                else
                    _forwardToActor.Forward(message);
                return true;
            }
        }

        internal class EchoTestActor : ActorBase
        {
            protected override bool Receive(object message)
            {
                Sender.Tell(message);
                return true;
            }
        }

        public class WrappedTerminated
        {
            private readonly Terminated _terminated;

            public WrappedTerminated(Terminated terminated)
            {
                _terminated = terminated;
            }

            public Terminated Terminated { get { return _terminated; } }
        }

        internal struct W
        {
            public W(ActorRef @ref)
                : this()
            {
                Ref = @ref;
            }

            public ActorRef Ref { get; private set; }
        }

        internal struct U
        {
            public U(ActorRef @ref)
                : this()
            {
                Ref = @ref;
            }

            public ActorRef Ref { get; private set; }
        }
        internal struct FF
        {
            public FF(Failed fail)
                : this()
            {
                Fail = fail;
            }

            public Failed Fail { get; private set; }
        }

        internal struct Latches : NoSerializationVerificationNeeded
        {

            public Latches(TestLatch t1, TestLatch t2)
                : this()
            {
                T1 = t1;
                T2 = t2;
            }
            public TestLatch T1 { get; set; }
            public TestLatch T2 { get; set; }
        }

    }
}