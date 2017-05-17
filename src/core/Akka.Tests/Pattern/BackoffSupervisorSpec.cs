//-----------------------------------------------------------------------
// <copyright file="BackoffSupervisorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Pattern;
using Akka.TestKit;
using Xunit;
using FluentAssertions;
using System.Threading;

namespace Akka.Tests.Pattern
{
    public class BackoffSupervisorSpec : AkkaSpec
    {
        #region Infrastructure
        internal class TestException : Exception
        {
        }

        internal class Child : ReceiveActor
        {
            private readonly IActorRef _probe;

            public Child(IActorRef probe)
            {
                _probe = probe;

                Receive<string>(str => str.Equals("boom"), message =>
                {
                    throw new TestException();
                    return;
                });

                ReceiveAny(msg =>
                {
                    _probe.Tell(msg);
                });
            }

            public static Props Props(IActorRef probe)
            {
                return Akka.Actor.Props.Create(() => new Child(probe));
            }
        }

        internal class ManualChild : ReceiveActor
        {
            private readonly IActorRef _probe;

            public ManualChild(IActorRef probe)
            {
                _probe = probe;

                Receive<string>(str => str.Equals("boom"), message =>
                {
                    throw new TestException();
                    return;
                });

                ReceiveAny(msg =>
                {
                    _probe.Tell(msg);
                    Context.Parent.Tell(BackoffSupervisor.Reset.Instance);
                });
            }

            public static Props Props(IActorRef probe)
            {
                return Akka.Actor.Props.Create(() => new ManualChild(probe));
            }
        }

        private BackoffOptions OnStopOptions() => OnStopOptions(Child.Props(TestActor));
        private BackoffOptions OnStopOptions(Props props) => Backoff.OnStop(props, "c1", 100.Milliseconds(), 3.Seconds(), 0.2);
        private BackoffOptions OnFailureOptions() => OnFailureOptions(Child.Props(TestActor));
        private BackoffOptions OnFailureOptions(Props props) => Backoff.OnFailure(props, "c1", 100.Milliseconds(), 3.Seconds(), 0.2);
        private IActorRef Create(BackoffOptions options) => Sys.ActorOf(BackoffSupervisor.Props(options));
        #endregion

        [Fact]
        public void BackoffSupervisor_must_start_child_again_when_it_stops_when_using_Backoff_OnStop()
        {
            var supervisor = Create(OnStopOptions());
            supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
            var c1 = ExpectMsg<BackoffSupervisor.CurrentChild>().Ref;
            Watch(c1);
            c1.Tell(PoisonPill.Instance);
            ExpectTerminated(c1);
            AwaitAssert(() =>
            {
                supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
                // new instance
                ExpectMsg<BackoffSupervisor.CurrentChild>().Ref.Should().NotBeSameAs(c1);
            });
        }

        [Fact]
        public void BackoffSupervisor_must_forward_messages_to_the_child()
        {
            Action<IActorRef> assertForward = supervisor =>
            {
                supervisor.Tell("hello");
                ExpectMsg("hello");
            };

            assertForward(Create(OnStopOptions()));
            assertForward(Create(OnFailureOptions()));
        }

        [Fact]
        public void BackoffSupervisor_must_support_custom_supervision_strategy()
        {
            Action<IActorRef> assertCustomStrategy = supervisor =>
            {
                supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
                var c1 = ExpectMsg<BackoffSupervisor.CurrentChild>().Ref;
                Watch(c1);
                c1.Tell("boom");
                ExpectTerminated(c1);
                AwaitAssert(() =>
                {
                    supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
                    // new instance
                    ExpectMsg<BackoffSupervisor.CurrentChild>().Ref.Should().NotBeSameAs(c1);
                });
            };

            // TODO: use FilterException
            EventFilter.Exception<TestException>().Expect(2, () =>
            {
                var stoppingStrategy = new OneForOneStrategy(ex =>
                {
                    if (ex is TestException)
                    {
                        return Directive.Stop;
                    }

                    return Directive.Escalate;
                });

                var restartingStrategy = new OneForOneStrategy(ex =>
                {
                    if (ex is TestException)
                    {
                        return Directive.Restart;
                    }

                    return Directive.Escalate;
                });

                assertCustomStrategy(Create(OnStopOptions().WithSupervisorStrategy(stoppingStrategy)));
                assertCustomStrategy(Create(OnFailureOptions().WithSupervisorStrategy(restartingStrategy)));
            });
        }

        [Fact]
        public void BackoffSupervisor_must_support_default_stopping_strategy_when_using_Backoff_OnStop()
        {
            // TODO: use FilterException
            EventFilter.Exception<TestException>().Expect(1, () =>
            {
                var supervisor = Create(OnStopOptions().WithDefaultStoppingStrategy());
                supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
                var c1 = ExpectMsg<BackoffSupervisor.CurrentChild>().Ref;
                Watch(c1);
                supervisor.Tell(BackoffSupervisor.GetRestartCount.Instance);
                ExpectMsg<BackoffSupervisor.RestartCount>().Count.Should().Be(0);

                c1.Tell("boom");
                ExpectTerminated(c1);
                AwaitAssert(() =>
                {
                    supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
                    // new instance
                    ExpectMsg<BackoffSupervisor.CurrentChild>().Ref.Should().NotBeSameAs(c1);
                });
                supervisor.Tell(BackoffSupervisor.GetRestartCount.Instance);
                ExpectMsg<BackoffSupervisor.RestartCount>().Count.Should().Be(1);
            });
        }

        [Fact]
        public void BackoffSupervisor_must_support_manual_reset()
        {
            Action<IActorRef> assertManualReset = supervisor =>
            {
                supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
                var c1 = ExpectMsg<BackoffSupervisor.CurrentChild>().Ref;
                Watch(c1);
                c1.Tell("boom");
                ExpectTerminated(c1);

                AwaitAssert(() =>
                {
                    supervisor.Tell(BackoffSupervisor.GetRestartCount.Instance);
                    ExpectMsg<BackoffSupervisor.RestartCount>().Count.Should().Be(1);
                });

                AwaitAssert(() =>
                {
                    supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
                    // new instance
                    ExpectMsg<BackoffSupervisor.CurrentChild>().Ref.Should().NotBeSameAs(c1);
                });

                // TODO: this Thread.Sleep should be removed
                Thread.Sleep(500);

                supervisor.Tell("hello");
                ExpectMsg("hello");

                // making sure the Reset is handled by supervisor
                supervisor.Tell("hello");
                ExpectMsg("hello");

                supervisor.Tell(BackoffSupervisor.GetRestartCount.Instance);
                ExpectMsg<BackoffSupervisor.RestartCount>().Count.Should().Be(0);
            };

            // TODO: use FilterException
            EventFilter.Exception<TestException>().Expect(2, () =>
            {
                var stoppingStrategy = new OneForOneStrategy(ex =>
                {
                    if (ex is TestException)
                    {
                        return Directive.Stop;
                    }

                    // TODO: should restart be there?
                    return Directive.Restart;
                });

                var restartingStrategy = new OneForOneStrategy(ex =>
                {
                    if (ex is TestException)
                    {
                        return Directive.Restart;
                    }

                    // TODO: should restart be there?
                    return Directive.Restart;
                });

                assertManualReset(
                    Create(OnStopOptions(ManualChild.Props(TestActor))
                        .WithManualReset()
                        .WithSupervisorStrategy(stoppingStrategy)));

                assertManualReset(
                    Create(OnFailureOptions(ManualChild.Props(TestActor))
                        .WithManualReset()
                        .WithSupervisorStrategy(restartingStrategy)));
            });
        }
    }
}