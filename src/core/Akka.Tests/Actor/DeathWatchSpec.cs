//-----------------------------------------------------------------------
// <copyright file="DeathWatchSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.TestKit;
using Akka.Tests.TestUtils;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Tests.Actor
{
    public class DeathWatchSpec : AkkaSpec
    {
        private IActorRef _supervisor;
        private IActorRef _terminal;

        public DeathWatchSpec()
        {
            _supervisor = Sys.ActorOf(Props.Create(() => new Supervisor(SupervisorStrategy.DefaultStrategy)), "watchers");
            _terminal = Sys.ActorOf(Props.Empty);
        }

        [Fact]
        public void DeathWatch_must_notify_with_one_Terminated_message_when_an_Actor_is_already_terminated()
        {
            var terminal = Sys.ActorOf(Props.Empty, "killed-actor");
            terminal.Tell(PoisonPill.Instance, TestActor);
            StartWatching(terminal);
            ExpectTerminationOf(terminal);
        }

        //        protected static string GetConfig()
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
            var actor = (ActorRefWithCell) Sys.ActorOf(Props.Empty, "killed-actor");
            Watch(actor);
            Sys.EventStream.Subscribe(TestActor, typeof (DeadLetter));

            actor.Tell(PoisonPill.Instance);
            ExpectMsg<Terminated>();

            actor.Tell(new Envelope("SomeUserMessage", TestActor));
            ExpectMsg<DeadLetter>(d => ((Envelope)d.Message).Message.Equals("SomeUserMessage"));

            //The actor should Terminate, exchange the mailbox to a DeadLetterMailbox and forward the user message to the DeadLetterMailbox
            
            actor.Underlying.AsInstanceOf<ActorCell>().Mailbox.ShouldBe(Sys.Mailboxes.DeadLetterMailbox);
        }

        [Fact]
        public void DeathWatch_must_notify_with_one_Terminated_message_when_actor_is_stopped()
        {
            const string msg = "hello";
            StartWatching(_terminal).Tell(msg);
            ExpectMsg(msg);
            _terminal.Tell(PoisonPill.Instance);
            ExpectTerminationOf(_terminal);
        }

        [Fact]
        public void DeathWatch_must_notify_with_one_custom_termination_message_when_actor_is_stopped()
        {
            const string msg = "hello";
            const string terminationMsg = "watchee terminated";
            StartWatchingWith(_terminal, terminationMsg).Tell(msg);
            ExpectMsg(msg);
            _terminal.Tell(PoisonPill.Instance);
            ExpectMsg(terminationMsg);
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

            Sys.Stop(monitor1);
            Sys.Stop(monitor2);
            Sys.Stop(monitor3);
        }

        [Fact]
        public void DeathWatch_must_notify_with_current_monitors_with_one_Terminated_message_when_Actor_is_stopped()
        {
            var monitor1 = StartWatching(_terminal);
            var monitor2 = Sys.ActorOf(Props.Create(() => new WatchAndUnwatchMonitor(_terminal, TestActor)).WithDeploy(Deploy.Local));
            var monitor3 = StartWatching(_terminal);

            monitor2.Tell("ping");
            ExpectMsg("pong");      // since Watch and Unwatch are asynchronous, we need some sync

            _terminal.Tell(PoisonPill.Instance);

            ExpectTerminationOf(_terminal);
            ExpectTerminationOf(_terminal);

            Sys.Stop(monitor1);
            Sys.Stop(monitor2);
            Sys.Stop(monitor3);
        }

        [Fact]
        public void DeathWatch_must_notify_with_a_Terminated_message_once_when_Actor_is_stopped_but_not_when_restarted()
        {
            EventFilter.Exception<ActorKilledException>().Expect(3, () =>
            {
                var timeout = TimeSpan.FromSeconds(5);
                var supervisor = Sys.ActorOf(Props.Create(() => new Supervisor(
                    new OneForOneStrategy(2, TimeSpan.FromSeconds(1), r => Directive.Restart))));

                var t1 = supervisor.Ask(Props.Create(() => new EchoTestActor()));
                t1.Wait(timeout);
                var terminal = t1.Result as LocalActorRef;
                var t2 = supervisor.Ask(CreateWatchAndForwarderProps(terminal, TestActor), timeout);
                t2.Wait(timeout);
                var monitor = t2.Result as IActorRef;

                terminal.Tell(Kill.Instance);
                terminal.Tell(Kill.Instance);

                var foo = terminal.Ask("foo", timeout).Result as string;
                foo.ShouldBe("foo");

                terminal.Tell(Kill.Instance);

                ExpectTerminationOf(terminal);
                terminal.IsTerminated.ShouldBe(true);

                Sys.Stop(supervisor);
            });
        }

        // See issue: #61
        [Fact]
        public void DeathWatch_must_fail_a_monitor_which_doesnt_handle_Terminated()
        {
            EventFilter.Exception<ActorKilledException>().And.Exception<DeathPactException>().Expect(2, () =>
            {
                var strategy = new FailedSupervisorStrategy(TestActor);
                _supervisor = Sys.ActorOf(Props.Create(() => new Supervisor(strategy)).WithDeploy(Deploy.Local));

                var failed = _supervisor.Ask(Props.Empty).Result as IActorRef;
                var brother = _supervisor.Ask(Props.Create(() => new BrotherActor(failed))).Result as IActorRef;

                StartWatching(brother);

                failed.Tell(Kill.Instance);
                var result = ReceiveWhile(TimeSpan.FromSeconds(5), msg =>
                {
                    var res = 0;
                    msg.Match()
                        .With<FF>(ff =>
                        {
                            if (ff.Fail.Cause is ActorKilledException && ff.Fail.Child == failed) res = 1;
                            if (ff.Fail.Cause is DeathPactException && ff.Fail.Child == brother) res = 2;
                        })
                        .With<WrappedTerminated>(x => res = x.Terminated.ActorRef == brother ? 3 : 0);
                    return res.ToString();
                }, 3);

                ((IInternalActorRef)TestActor).IsTerminated.ShouldBe(false);
                result.ShouldOnlyContainInOrder("1", "2", "3");
            });
        }

        [Fact]
        public void DeathWatch_must_be_able_to_watch_child_with_the_same_name_after_the_old_one_died()
        {
            var parent = Sys.ActorOf(Props.Create(() => new KnobActor(TestActor)).WithDeploy(Deploy.Local));

            parent.Tell(Knob);
            ExpectMsg(Bonk);
            parent.Tell(Knob);
            ExpectMsg(Bonk);
        }

        [Fact]
        public void DeathWatch_must_notify_only_when_watching()
        {
            var subject = Sys.ActorOf(Props.Create(() => new EchoActor(_terminal)));
            ((IInternalActorRef)TestActor).SendSystemMessage(new DeathWatchNotification(subject, true, false));
            ExpectNoMsg(TimeSpan.FromSeconds(3));
        }

        // See issue: #61
        [Fact]
        public void DeathWatch_must_discard_Terminated_when_unwatched_between_sysmsg_and_processing()
        {
            var t1 = CreateTestLatch(1);
            var t2 = CreateTestLatch(1);
            var p = CreateTestProbe();
            var w = Sys.ActorOf(Props.Create(() => new Watcher()).WithDeploy(Deploy.Local), "testWatcher");

            w.Tell(new W(p.Ref));
            w.Tell(new Latches(t1, t2));
            t1.Ready(TimeSpan.FromSeconds(3));
            Watch(p.Ref);
            Sys.Stop(p.Ref);
            ExpectTerminated(p.Ref);
            w.Tell(new U(p.Ref));
            t2.CountDown();

            // now the Watcher will
            // - process the DeathWatchNotification and enqueue Terminated
            // - process the unwatch command
            // - process the Terminated
            // If it receives the Terminated it will die, which in fact it should not
            w.Tell(new Identify(null));
            ExpectMsg<ActorIdentity>(ai => ai.Subject == w);
            w.Tell(new Identify(null));
            ExpectMsg<ActorIdentity>(ai => ai.Subject == w);
        }

        private void ExpectTerminationOf(IActorRef actorRef)
        {
            ExpectMsg<WrappedTerminated>(w => ReferenceEquals(w.Terminated.ActorRef, actorRef));
        }

        private IActorRef StartWatching(IActorRef target)
        {
            var task = _supervisor.Ask(CreateWatchAndForwarderProps(target, TestActor), TimeSpan.FromSeconds(3));
            task.Wait(TimeSpan.FromSeconds(3));
            return (IActorRef)task.Result;
        }

        private IActorRef StartWatchingWith(IActorRef target, object message)
        {
            var task = _supervisor.Ask(CreateWatchWithAndForwarderProps(target, TestActor, message), TimeSpan.FromSeconds(3));
            task.Wait(TimeSpan.FromSeconds(3));
            return (IActorRef)task.Result;
        }

        private Props CreateWatchAndForwarderProps(IActorRef target, IActorRef forwardToActor)
        {
            return Props.Create(() => new WatchAndForwardActor(target, forwardToActor));
        }

        private Props CreateWatchWithAndForwarderProps(IActorRef target, IActorRef forwardToActor, object message)
        {
            return Props.Create(() => new WatchWithAndForwardActor(target, forwardToActor, message));
        }

        internal class BrotherActor : ReceiveActor
        {
            public BrotherActor(IActorRef failed)
            {
                Context.Watch(failed);
            }
        }

        internal class FailedSupervisorStrategy : OneForOneStrategy
        {
            public IActorRef TestActor { get; private set; }

            public FailedSupervisorStrategy(IActorRef testActor) : base(DefaultDecider)
            {
                TestActor = testActor;
            }

            public override void ProcessFailure(IActorContext context, bool restart, IActorRef child, Exception cause, ChildRestartStats stats, IReadOnlyCollection<ChildRestartStats> children)
            {
                TestActor.Tell(new FF(new Failed(child, cause, stats.Uid)), child);
                base.ProcessFailure(context, restart, child, cause, stats, children);
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
            private readonly IActorRef _testActor;

            public KnobActor(IActorRef testActor)
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
                                    Context.UnbecomeStacked();
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
            private readonly IActorRef _testActor;

            public WatchAndUnwatchMonitor(IActorRef terminal, IActorRef testActor)
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

        internal class Watcher : ReceiveActor
        {
            public Watcher()
            {
                Receive<W>(w => Context.Watch(w.Ref));
                Receive<U>(w => Context.Unwatch(w.Ref));
                Receive<Latches>(x =>
                    {
                        x.T1.CountDown();
                        x.T2.Ready(TimeSpan.FromSeconds(3));
                    });
            }
        }

        internal class WatchAndForwardActor : ActorBase
        {
            private readonly IActorRef _forwardToActor;

            public WatchAndForwardActor(IActorRef watchedActor, IActorRef forwardToActor)
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

        internal class WatchWithAndForwardActor : ActorBase
        {
            private readonly IActorRef _forwardToActor;

            public WatchWithAndForwardActor(IActorRef watchedActor, IActorRef forwardToActor, object message)
            {
                _forwardToActor = forwardToActor;
                Context.WatchWith(watchedActor, message);
            }

            protected override bool Receive(object message)
            {
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
            public W(IActorRef @ref)
                : this()
            {
                Ref = @ref;
            }

            public IActorRef Ref { get; private set; }
        }

        internal struct U
        {
            public U(IActorRef @ref)
                : this()
            {
                Ref = @ref;
            }

            public IActorRef Ref { get; private set; }
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

        internal struct Latches : INoSerializationVerificationNeeded
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

        /// <summary>
        /// Used for testing Ask / reply behaviors
        /// </summary>
        public class EchoActor : UntypedActor
        {
            private IActorRef _testActor;

            public EchoActor(IActorRef testActorRef)
            {
                _testActor = testActorRef;
            }

            protected override void OnReceive(object message)
            {
                Sender.Forward(message);
                _testActor.Tell(message, Sender);
            }
        }
    }
}

