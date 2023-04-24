//-----------------------------------------------------------------------
// <copyright file="DeathWatchSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.Tests.TestUtils;
using Akka.Tests.Util;
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
        public async Task DeathWatch_must_notify_with_one_Terminated_message_when_an_Actor_is_already_terminated()
        {
            var terminal = Sys.ActorOf(Props.Empty, "killed-actor");
            terminal.Tell(PoisonPill.Instance, TestActor);
            await StartWatching(terminal);
            await ExpectTerminationOf(terminal);
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
        public async Task Bug209_any_user_messages_following_a_Terminate_message_should_be_forwarded_to_DeadLetterMailbox()
        {
            var actor = (ActorRefWithCell) Sys.ActorOf(Props.Empty, "killed-actor");
            Watch(actor);
            Sys.EventStream.Subscribe(TestActor, typeof (DeadLetter));

            actor.Tell(PoisonPill.Instance);
            await ExpectMsgAsync<Terminated>();

            actor.Tell(new Envelope("SomeUserMessage", TestActor));
            await ExpectMsgAsync<DeadLetter>(d => ((Envelope)d.Message).Message.Equals("SomeUserMessage"));

            //The actor should Terminate, exchange the mailbox to a DeadLetterMailbox and forward the user message to the DeadLetterMailbox
            
            actor.Underlying.AsInstanceOf<ActorCell>().Mailbox.ShouldBe(Sys.Mailboxes.DeadLetterMailbox);
        }

        [Fact]
        public async Task DeathWatch_must_notify_with_one_Terminated_message_when_actor_is_stopped()
        {
            const string msg = "hello";
            (await StartWatching(_terminal)).Tell(msg);
            await ExpectMsgAsync(msg);
            _terminal.Tell(PoisonPill.Instance);
            await ExpectTerminationOf(_terminal);
        }

        [Fact]
        public async Task DeathWatch_must_notify_with_one_custom_termination_message_when_actor_is_stopped()
        {
            const string msg = "hello";
            const string terminationMsg = "watchee terminated";
            (await StartWatchingWith(_terminal, terminationMsg)).Tell(msg);
            await ExpectMsgAsync(msg);
            _terminal.Tell(PoisonPill.Instance);
            await ExpectMsgAsync(terminationMsg);
        }

        [Fact]
        public async Task DeathWatch_must_notify_with_all_monitors_with_one_Terminated_message_when_Actor_is_stopped()
        {
            var monitor1 = await StartWatching(_terminal);
            var monitor2 = await StartWatching(_terminal);
            var monitor3 = await StartWatching(_terminal);

            _terminal.Tell(PoisonPill.Instance);

            await ExpectTerminationOf(_terminal);
            await ExpectTerminationOf(_terminal);
            await ExpectTerminationOf(_terminal);

            Sys.Stop(monitor1);
            Sys.Stop(monitor2);
            Sys.Stop(monitor3);
        }

        [Fact]
        public async Task DeathWatch_must_notify_with_current_monitors_with_one_Terminated_message_when_Actor_is_stopped()
        {
            var monitor1 = await StartWatching(_terminal);
            var monitor2 = Sys.ActorOf(Props.Create(() => new WatchAndUnwatchMonitor(_terminal, TestActor)).WithDeploy(Deploy.Local));
            var monitor3 = await StartWatching(_terminal);

            monitor2.Tell("ping");
            await ExpectMsgAsync("pong");      // since Watch and Unwatch are asynchronous, we need some sync

            _terminal.Tell(PoisonPill.Instance);

            await ExpectTerminationOf(_terminal);
            await ExpectTerminationOf(_terminal);

            Sys.Stop(monitor1);
            Sys.Stop(monitor2);
            Sys.Stop(monitor3);
        }

        [Fact]
        public async Task DeathWatch_must_notify_with_a_Terminated_message_once_when_Actor_is_stopped_but_not_when_restarted()
        {
            await EventFilter.Exception<ActorKilledException>().ExpectAsync(3, async () =>
            {
                var timeout = TimeSpan.FromSeconds(5);
                var supervisor = Sys.ActorOf(Props.Create(() => new Supervisor(
                    new OneForOneStrategy(2, TimeSpan.FromSeconds(1), r => Directive.Restart))));

                var t1 = supervisor.Ask(Props.Create(() => new EchoTestActor()));
                await t1.AwaitWithTimeout(timeout);
                var terminal = (LocalActorRef) t1.Result;
                
                var t2 = supervisor.Ask(CreateWatchAndForwarderProps(terminal, TestActor));
                await t2.AwaitWithTimeout(timeout);
                var monitor = (IActorRef) t2.Result;

                terminal.Tell(Kill.Instance);
                terminal.Tell(Kill.Instance);

                var foo = (await terminal.Ask("foo", timeout)) as string;
                foo.ShouldBe("foo");

                terminal.Tell(Kill.Instance);

                await ExpectTerminationOf(terminal);
                terminal.IsTerminated.ShouldBe(true);

                Sys.Stop(supervisor);
            });
        }

        // See issue: #61
        [Fact]
        public async Task DeathWatch_must_fail_a_monitor_which_doesnt_handle_Terminated()
        {
            await EventFilter.Exception<ActorKilledException>().And.Exception<DeathPactException>().ExpectAsync(2, async() =>
            {
                var strategy = new FailedSupervisorStrategy(TestActor);
                _supervisor = Sys.ActorOf(Props.Create(() => new Supervisor(strategy)).WithDeploy(Deploy.Local));

                var failed = (IActorRef) (await _supervisor.Ask(Props.Empty));
                var brother = (IActorRef) (await _supervisor.Ask(Props.Create(() => new BrotherActor(failed))));

                await StartWatching(brother);

                failed.Tell(Kill.Instance);
                var result = await ReceiveWhileAsync(TimeSpan.FromSeconds(5), msg =>
                {
                    var res = 0;
                    switch (msg)
                    {
                        case FF ff:
                            switch (ff.Fail.Cause)
                            {
                                case ActorKilledException _ when ReferenceEquals(ff.Fail.Child, failed):
                                    res = 1;
                                    break;
                                case DeathPactException _ when ReferenceEquals(ff.Fail.Child, brother):
                                    res = 2;
                                    break;
                            }
                            break;
                        
                        case WrappedTerminated x:
                            res = ReferenceEquals(x.Terminated.ActorRef, brother) ? 3 : 0;
                            break;
                    }
                    return res.ToString();
                }, 3).ToListAsync();

                ((IInternalActorRef)TestActor).IsTerminated.ShouldBe(false);
                result.ShouldOnlyContainInOrder("1", "2", "3");
            });
        }

        [Fact]
        public async Task DeathWatch_must_be_able_to_watch_child_with_the_same_name_after_the_old_one_died()
        {
            var parent = Sys.ActorOf(Props.Create(() => new KnobActor(TestActor)).WithDeploy(Deploy.Local));

            parent.Tell(Knob);
            await ExpectMsgAsync(Bonk);
            parent.Tell(Knob);
            await ExpectMsgAsync(Bonk);
        }

        [Fact]
        public async Task DeathWatch_must_notify_only_when_watching()
        {
            var subject = Sys.ActorOf(Props.Create(() => new EchoActor(_terminal)));
            ((IInternalActorRef)TestActor).SendSystemMessage(new DeathWatchNotification(subject, true, false));
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(3));
        }

        // See issue: #61
        [Fact]
        public async Task DeathWatch_must_discard_Terminated_when_unwatched_between_sysmsg_and_processing()
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
            await ExpectTerminatedAsync(p.Ref);
            w.Tell(new U(p.Ref));
            t2.CountDown();

            // now the Watcher will
            // - process the DeathWatchNotification and enqueue Terminated
            // - process the unwatch command
            // - process the Terminated
            // If it receives the Terminated it will die, which in fact it should not
            w.Tell(new Identify(null));
            await ExpectMsgAsync<ActorIdentity>(ai => ai.Subject == w);
            w.Tell(new Identify(null));
            await ExpectMsgAsync<ActorIdentity>(ai => ai.Subject == w);
        }

        private async Task ExpectTerminationOf(IActorRef actorRef)
        {
            await ExpectMsgAsync<WrappedTerminated>(w => ReferenceEquals(w.Terminated.ActorRef, actorRef));
        }

        private async Task<IActorRef> StartWatching(IActorRef target)
        {
            var task = _supervisor.Ask(CreateWatchAndForwarderProps(target, TestActor));
            await task.AwaitWithTimeout(TimeSpan.FromSeconds(3));
            return (IActorRef)task.Result;
        }

        private async Task<IActorRef> StartWatchingWith(IActorRef target, object message)
        {
            var task = _supervisor.Ask(CreateWatchWithAndForwarderProps(target, TestActor, message), TimeSpan.FromSeconds(3));
            await task.AwaitWithTimeout(TimeSpan.FromSeconds(3));
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
        internal class KnobKidActor : ReceiveActor
        {
            public KnobKidActor()
            {
                Receive<string>(s =>
                {
                    if (s == Knob)
                    {
                        Context.Stop(Self);
                    }
                });
            }
        }
        internal class KnobActor : ReceiveActor
        {
            private readonly IActorRef _testActor;

            public KnobActor(IActorRef testActor)
            {
                _testActor = testActor;

                Receive<string>(x =>
                {
                    if (x != Knob) 
                        return;
                    
                    var kid = Context.ActorOf(Props.Create(() => new KnobKidActor()), "kid");
                    Context.Watch(kid);
                    kid.Forward(Knob);
                    Context.Become(msg =>
                    {
                        if (msg is Terminated y && y.ActorRef == kid)
                        {
                            _testActor.Tell(Bonk);
                            Context.UnbecomeStacked();
                        }

                        return true;
                    });
                });
            }
        }

        internal class WatchAndUnwatchMonitor : ReceiveActor
        {
            private readonly IActorRef _testActor;

            public WatchAndUnwatchMonitor(IActorRef terminal, IActorRef testActor)
            {
                _testActor = testActor;
                Context.Watch(terminal);
                Context.Unwatch(terminal);

                Receive<string>(x =>
                {
                    if (x == "ping")
                        _testActor.Tell("pong");
                });

                Receive<Terminated>(x => _testActor.Tell(new WrappedTerminated(x)));
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

        internal class WatchAndForwardActor : ReceiveActor
        {
            private readonly IActorRef _forwardToActor;

            public WatchAndForwardActor(IActorRef watchedActor, IActorRef forwardToActor)
            {
                _forwardToActor = forwardToActor;
                Context.Watch(watchedActor);

                Receive<Terminated>(terminated => _forwardToActor.Forward(new WrappedTerminated(terminated)));
                ReceiveAny(message => _forwardToActor.Forward(message));
            }
        }

        internal class WatchWithAndForwardActor : ReceiveActor
        {
            private readonly IActorRef _forwardToActor;

            public WatchWithAndForwardActor(IActorRef watchedActor, IActorRef forwardToActor, object message)
            {
                _forwardToActor = forwardToActor;
                Context.WatchWith(watchedActor, message);
                ReceiveAny(_forwardToActor.Forward);
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
            public WrappedTerminated(Terminated terminated)
            {
                Terminated = terminated;
            }

            public Terminated Terminated { get; }
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

