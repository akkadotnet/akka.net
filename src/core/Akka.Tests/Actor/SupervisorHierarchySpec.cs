//-----------------------------------------------------------------------
// <copyright file="SupervisorHierarchySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Tests.TestUtils;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Tests.Actor
{
    public class SupervisorHierarchySpec : AkkaSpec
    {
        private class CountDownActor : ReceiveActor
        {
            private readonly CountdownEvent _restartsCountdown;
            private readonly SupervisorStrategy _supervisorStrategy;

            public CountDownActor(CountdownEvent restartsCountdown, SupervisorStrategy supervisorStrategy)
            {
                _restartsCountdown = restartsCountdown;
                _supervisorStrategy = supervisorStrategy;
                Receive<Props>(p => Sender.Tell(Context.ActorOf(p)));
                Receive<PropsWithName>(p => Sender.Tell(Context.ActorOf(p.Props, p.Name)));
            }

            protected override void PreRestart(Exception reason, object message)
            {
                //Test relies on keeping children around during restart so we will not call base, 
                //which by default it disposes of all children and then calls postStop()
            }

            protected override void PostRestart(Exception reason)
            {
                _restartsCountdown.Signal();
            }

            protected override SupervisorStrategy SupervisorStrategy()
            {
                return _supervisorStrategy;
            }
        }

        private class Resumer : TestReceiveActor
        {
            public Resumer()
            {
                Receive<string>(s => s.StartsWith("spawn:"), s => Sender.Tell(Context.ActorOf<Resumer>(s.Substring(6))));
                Receive("spawn", _ => Sender.Tell(Context.ActorOf<Resumer>()));
                Receive("fail", _ => { throw new Exception("expected"); });
                Receive("ping", _ => Sender.Tell("pong"));
            }

            protected override SupervisorStrategy SupervisorStrategy()
            {
                return new OneForOneStrategy(_ => Directive.Resume);
            }
        }

        private class ResumerAsync : TestReceiveActor
        {
            public ResumerAsync()
            {
                ReceiveAsync<string>(s => s.StartsWith("spawn:"), async s => Sender.Tell(Context.ActorOf<ResumerAsync>(s.Substring(6))));
                ReceiveAsync<string>(s => s.Equals("spawn"), async _ => Sender.Tell(Context.ActorOf<ResumerAsync>()));
                ReceiveAsync<string>(s => s.Equals("fail"), async _ => { throw new Exception("expected"); });
                ReceiveAsync<string>(s => s.Equals("ping"), async _ => Sender.Tell("pong"));
            }

            protected override SupervisorStrategy SupervisorStrategy()
            {
                return new OneForOneStrategy(_ => Directive.Resume);
            }
        }

        private class Failure : Exception
        {
            public Directive Directive { get; set; }
            public bool Stop { get; set; }
            public int Depth { get; set; }
            public int FailPre { get; set; }
            public int FailPost { get; set; }
            public int FailConstr { get; set; }
            public int StopKids { get; set; }

            public Failure(Directive directive, bool stop, int depth, int failPre, int failPost, int failConstr, int stopKids)
                : base("failure")
            {
                Directive = directive;
                Stop = stop;
                Depth = depth;
                FailPre = failPre;
                FailPost = failPost;
                FailConstr = failConstr;
                StopKids = stopKids;
            }
        }

        public SupervisorHierarchySpec()
            : base(FullDebugConfig)
        {

        }

        [Fact]
        public void A_supervisor_hierarchy_must_Restart_Manager_And_Workers_In_AllForOne()
        {
            var countDown = new CountdownEvent(4);
            SupervisorStrategy strategy = new OneForOneStrategy(_ => Directive.Restart);
            var boss = ActorOf(Props.Create(() => new Supervisor(strategy)), "boss");

            Func<Exception, Directive> decider = _ => { return Directive.Escalate; };
            var managerProps = new PropsWithName(Props.Create(() => new CountDownActor(countDown, new AllForOneStrategy(decider))), "manager");
            var manager = boss.Ask<IActorRef>(managerProps, TestKitSettings.DefaultTimeout).Result;

            var workerProps = Props.Create(() => new CountDownActor(countDown, SupervisorStrategy.DefaultStrategy));
            var worker1 = manager.Ask<IActorRef>(new PropsWithName(workerProps, "worker1"), TestKitSettings.DefaultTimeout).Result;
            var worker2 = manager.Ask<IActorRef>(new PropsWithName(workerProps, "worker2"), TestKitSettings.DefaultTimeout).Result;
            var worker3 = manager.Ask<IActorRef>(new PropsWithName(workerProps, "worker3"), TestKitSettings.DefaultTimeout).Result;

            EventFilter.Exception<ActorKilledException>().ExpectOne(() =>
            {
                worker1.Tell(Kill.Instance);
                // manager + all workers should be restarted by only killing a worker
                // manager doesn't trap exits, so boss will restart manager

                countDown.Wait(TimeSpan.FromSeconds(5)).ShouldBe(true);

            });

        }

        [Fact]
        public void A_supervisor_must_send_notifications_to_supervisor_when_permanent_failure()
        {
            var countDownMessages = new CountdownEvent(1);
            var countDownMax = new CountdownEvent(1);
            var boss = ActorOf((cfg, ctx) =>
            {
                var crasher = ctx.ActorOf(Props.Create(() => new CountDownActor(countDownMessages, SupervisorStrategy.DefaultStrategy)), "crasher");
                cfg.Receive("killCrasher", (s, context) => crasher.Tell(Kill.Instance));
                cfg.Receive<Terminated>((terminated, context) => countDownMax.Signal());
                cfg.Strategy = new OneForOneStrategy(1, TimeSpan.FromSeconds(5), e => Directive.Restart);
                ctx.Watch(crasher);
            }, "boss");

            //We have built this hierarchy:
            //     boss
            //      |
            //    crasher

            //We send "killCrasher" to boss, which in turn will send Kill to crasher making it crash.
            //Crasher will be restarted, and during PostRestart countDownMessages will count down.
            //We then send another "killCrasher", which again will send Kill to crasher. It crashes,
            //decider says it should be restarted but since we specified maximum 1 restart/5seconds it will be 
            //permanently stopped. Boss, which watches crasher, receives Terminated, and counts down countDownMax
            EventFilter.Exception<ActorKilledException>().Expect(2, () =>
            {
                boss.Tell("killCrasher");
                boss.Tell("killCrasher");
            });
            countDownMessages.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue();
            countDownMax.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue();
        }

        private void Helper_A_supervisor_hierarchy_must_resume_children_after_Resume<T>() 
            where T : ActorBase, new()
        {
            //Build this hierarchy:
            //     boss
            //      |
            //    middle
            //      |
            //    worker
            var name = typeof(T).Name;
            var boss = ActorOf<T>(name);
            boss.Tell("spawn:middle");
            var middle = ExpectMsg<IActorRef>();
            middle.Tell("spawn:worker");
            var worker = ExpectMsg<IActorRef>();

            //Check everything is in place by sending ping to worker and expect it to respond with pong
            worker.Tell("ping");
            ExpectMsg("pong");
            EventFilter.Warning("expected").ExpectOne(() => //expected exception is thrown by the boss when it crashes
            {
                middle.Tell("fail");    //Throws an exception, and then it's resumed
            });

            //verify that middle answers
            middle.Tell("ping");
            ExpectMsg("pong");

            //verify worker (child to middle) is up
            worker.Tell("ping");
            ExpectMsg("pong");
        }

        [Fact]
        public void A_supervisor_hierarchy_must_resume_children_after_Resume()
        {
            Helper_A_supervisor_hierarchy_must_resume_children_after_Resume<Resumer>();
            Helper_A_supervisor_hierarchy_must_resume_children_after_Resume<ResumerAsync>();
        }

        [Fact]
        public void A_supervisor_hierarchy_must_suspend_children_while_failing()
        {
            var latch = CreateTestLatch();
            var slowResumer = ActorOf(c =>
            {
                c.Strategy = new OneForOneStrategy(e => { latch.Ready(Dilated(TimeSpan.FromSeconds(4))); return Directive.Resume; });
                c.Receive<string>(s => s.StartsWith("spawn:"), (s, ctx) => ctx.Sender.Tell(ctx.ActorOf<Resumer>(s.Substring(6))));
                c.Receive("spawn", (s, ctx) => ctx.Sender.Tell(ctx.ActorOf<Resumer>()));
            }, "slowResumer");

            //Build this hierarchy:
            //  slowResumer
            //      |
            //     boss
            //      |
            //    middle
            //      |
            //    worker
            slowResumer.Tell("spawn:boss");
            var boss = ExpectMsg<IActorRef>();
            boss.Tell("spawn:middle");
            var middle = ExpectMsg<IActorRef>();
            middle.Tell("spawn:worker");
            var worker = ExpectMsg<IActorRef>();

            //Check everything is in place by sending ping to worker and expect it to respond with pong
            worker.Tell("ping");
            ExpectMsg("pong");
            EventFilter.Warning("expected").ExpectOne(() => //expected exception is thrown by the boss when it crashes
            {
                //Let boss crash, this means any child under boss should be suspended, so we wait for worker to become suspended.                
                boss.Tell("fail");
                AwaitCondition(() => ((LocalActorRef)worker).Cell.Mailbox.IsSuspended());

                //At this time slowresumer is currently handling the failure, in supervisestrategy, waiting for latch to be opened
                //We verify that no message is handled by worker, by sending it a ping
                //Normally it would respond with a pong, but since it's suspended nothing will happen.
                worker.Tell("ping");
                ExpectNoMsg(TimeSpan.FromSeconds(1));

                //By counting down the latch slowResumer will continue in the supervisorstrategy and will return Resume.
                latch.CountDown();
            });

            //Check that all children, and especially worker is resumed. It should receive the ping and respond with a pong
            ExpectMsg("pong", TimeSpan.FromMinutes(10));
        }

        [Fact]
        public void A_supervisor_hierarchy_must_handle_failure_in_creation_when_supervision_strategy_returns_Resume_and_Restart()
        {
            var createAttempt = new AtomicCounter(0);
            var preStartCalled = new AtomicCounter(0);
            var postRestartCalled = new AtomicCounter(0);

            EventFilter.Exception<Failure>()
                .And.Exception<InvalidOperationException>("OH NO!")
                .And.Error(start: "changing Recreate into Create")
                .And.Error(start: "changing Resume into Create")
                .Mute(() =>
                {
                    //Create:
                    // failresumer
                    //    |
                    // failingChild
                    //    | (sometimes)
                    // workingChild
                    var failresumer = ActorOf((resumerDsl, context) =>
                    {
                        resumerDsl.Strategy = new OneForOneStrategy(ex => ex is ActorInitializationException ? (createAttempt.Current % 2 == 0 ? Directive.Resume : Directive.Restart) : Directive.Escalate);
                        var failingChild = context.ActorOf((childDsl, childContext) =>
                        {
                            var ca = createAttempt.IncrementAndGet();
                            if (ca <= 6 && ca % 3 == 0)
                                childContext.ActorOf(BlackHoleActor.Props, "workingChild" + ca);
                            if (ca < 6)
                                throw new InvalidOperationException("OH NO!");
                            childDsl.OnPreStart = _ => preStartCalled.IncrementAndGet();
                            childDsl.OnPostRestart = (e, _) => postRestartCalled.IncrementAndGet();
                            childDsl.ReceiveAny((m, actorContext) => actorContext.Sender.Tell(m));
                        }, "failingChild");
                        resumerDsl.ReceiveAny((m, _) => failingChild.Forward(m));
                    }, "failresumer");

                    //Send failresumer some meatballs. This message will be forwarded to failingChild
                    failresumer.Tell("Köttbullar");
                    ExpectMsg("Köttbullar");
                });

            createAttempt.Current.ShouldBe(6);
            preStartCalled.Current.ShouldBe(1);
            postRestartCalled.Current.ShouldBe(0);
        }
    }
}

