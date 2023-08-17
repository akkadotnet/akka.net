//-----------------------------------------------------------------------
// <copyright file="BackoffOnRestartSupervisorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using Akka.Actor;
using Akka.Pattern;
using Akka.TestKit;
using Xunit;
using FluentAssertions;
using System.Threading;
using FluentAssertions.Extensions;
using System.Threading.Tasks;

namespace Akka.Tests.Pattern
{
    public class BackoffOnRestartSupervisorSpec : AkkaSpec
    {
        #region Infrastructure
        public class TestException : Exception
        {
            public TestException(string message) : base(message)
            {
            }
        }

        public class StoppingException : TestException
        {
            public StoppingException() : base("stopping exception")
            {
            }
        }

        public class NormalException : TestException
        {
            public NormalException() : base("normal exception")
            {
            }
        }

        public class TestActor : ReceiveActor
        {
            private readonly IActorRef _probe;

#pragma warning disable CS0162 // Disabled because without the return, the compiler complains about ambigious reference between Receive<T>(Action<T>,Predicate<T>) and Receive<T>(Predicate<T>,Action<T>)
            public TestActor(IActorRef probe)
            {
                _probe = probe;

                _probe.Tell("STARTED");

                Receive<string>(str => str.Equals("DIE"), _ => Context.Stop(Self));

                Receive<string>(str => str.Equals("THROW"), _ =>
                {
                    throw new NormalException();
                    return;
                });

                Receive<string>(str => str.Equals("THROW_STOPPING_EXCEPTION"), _ =>
                {
                    throw new StoppingException();
                    return;
                });

                Receive<(string, string)>(str => str.Item1.Equals("TO_PARENT"), msg =>
                {
                    Context.Parent.Tell(msg.Item2);
                });

                ReceiveAny(other => _probe.Tell(other));
            }
#pragma warning restore CS0162

            public static Props Props(IActorRef probe)
            {
                return Akka.Actor.Props.Create(() => new TestActor(probe));
            }
        }

        public class TestParentActor : ReceiveActor
        {
            private readonly IActorRef _probe;

            public TestParentActor(IActorRef probe, Props supervisorProps)
            {
                _probe = probe;
                var supervisor = Context.ActorOf(supervisorProps);

                ReceiveAny(other => _probe.Forward(other));
            }

            public static Props Props(IActorRef probe, Props supervisorProps)
            {
                return Akka.Actor.Props.Create(() => new TestParentActor(probe, supervisorProps));
            }
        }

        public class SlowlyFailingActor : ReceiveActor
        {
            private readonly TestLatch _latch;

#pragma warning disable CS0162 // Disabled because without the return, the compiler complains about ambigious reference between Receive<T>(Action<T>,Predicate<T>) and Receive<T>(Predicate<T>,Action<T>)
            public SlowlyFailingActor(TestLatch latch)
            {
                _latch = latch;

                Receive<string>(str => str.Equals("THROW"), _ =>
                {
                    Sender.Tell("THROWN");
                    throw new NormalException();
                    return;
                });

                Receive<string>(str => str.Equals("PING"), _ =>
                {
                    Sender.Tell("PONG");
                });
            }
#pragma warning restore CS0162

            protected override void PostStop()
            {
                _latch.Ready(3.Seconds());
            }

            public static Props Props(TestLatch latch)
            {
                return Akka.Actor.Props.Create(() => new SlowlyFailingActor(latch));
            }
        }

        private Props SupervisorProps(IActorRef probeRef)
        {
            var options = Backoff.OnFailure(TestActor.Props(probeRef), "someChildName", 200.Milliseconds(), 10.Seconds(), 0.0, -1)
                .WithManualReset()
                .WithSupervisorStrategy(new OneForOneStrategy(4, TimeSpan.FromSeconds(30), ex => ex is StoppingException 
                    ? Directive.Stop 
                    : SupervisorStrategy.DefaultStrategy.Decider.Decide(ex)));

            return BackoffSupervisor.Props(options);
        }
        #endregion

        [Fact]
        public async Task BackoffOnRestartSupervisor_must_terminate_when_child_terminates()
        {
            var probe = CreateTestProbe();
            var supervisor = Sys.ActorOf(SupervisorProps(probe.Ref));
            await probe.ExpectMsgAsync("STARTED");

            probe.Watch(supervisor);
            supervisor.Tell("DIE");
            await probe.ExpectTerminatedAsync(supervisor);
        }

        [Fact]
        public async Task BackoffOnRestartSupervisor_must_restart_the_child_with_an_exponential_back_off()
        {
            var probe = CreateTestProbe();
            var supervisor = Sys.ActorOf(SupervisorProps(probe.Ref));
            await probe.ExpectMsgAsync("STARTED");

            await EventFilter.Exception<TestException>().ExpectAsync(3, async() =>
            {
                // Exponential back off restart test
                await probe.WithinAsync(TimeSpan.FromSeconds(1.4), 2.Seconds(), async () =>
                {
                    supervisor.Tell("THROW");
                    // numRestart = 0 ~ 200 millis
                    await probe.ExpectMsgAsync<string>(300.Milliseconds(), "STARTED");

                    supervisor.Tell("THROW");
                    // numRestart = 1 ~ 400 millis
                    await probe.ExpectMsgAsync<string>(500.Milliseconds(), "STARTED");

                    supervisor.Tell("THROW");
                    // numRestart = 2 ~ 800 millis
                    await probe.ExpectMsgAsync<string>(900.Milliseconds(), "STARTED");
                });
            });

            // Verify that we only have one child at this point by selecting all the children
            // under the supervisor and broadcasting to them.
            // If there exists more than one child, we will get more than one reply.
            var supervisionChildSelection = Sys.ActorSelection(supervisor.Path / "*");
            supervisionChildSelection.Tell("testmsg", probe.Ref);
            await probe.ExpectMsgAsync("testmsg");
            await probe.ExpectNoMsgAsync();
        }

        [Fact]
        public async Task BackoffOnRestartSupervisor_must_stop_on_exceptions_as_dictated_by_the_supervisor_strategy()
        {
            var probe = CreateTestProbe();
            var supervisor = Sys.ActorOf(SupervisorProps(probe.Ref));
            await probe.ExpectMsgAsync("STARTED");

            await EventFilter.Exception<TestException>().ExpectAsync(1, async() =>
            {
                probe.Watch(supervisor);
                // This should cause the supervisor to stop the child actor and then
                // subsequently stop itself.
                supervisor.Tell("THROW_STOPPING_EXCEPTION");
                await probe.ExpectTerminatedAsync(supervisor);
            });
        }

        [Fact]
        public async Task BackoffOnRestartSupervisor_must_forward_messages_from_the_child_to_the_parent_of_the_supervisor()
        {
            var probe = CreateTestProbe();
            var parent = Sys.ActorOf(TestParentActor.Props(probe.Ref, SupervisorProps(probe.Ref)));
            await probe.ExpectMsgAsync("STARTED");
            var child = probe.LastSender;

            child.Tell(("TO_PARENT", "TEST_MESSAGE"));
            await probe.ExpectMsgAsync("TEST_MESSAGE");
        }

        [Fact]
        public async Task BackoffOnRestartSupervisor_must_accept_commands_while_child_is_terminating()
        {
            var postStopLatch = CreateTestLatch(1);
            var options = Backoff.OnFailure(SlowlyFailingActor.Props(postStopLatch), "someChildName", 1.Ticks(), 1.Ticks(), 0.0, -1)
                .WithSupervisorStrategy(new OneForOneStrategy(ex => ex is StoppingException 
                    ? Directive.Stop 
                    : SupervisorStrategy.DefaultStrategy.Decider.Decide(ex)));
            var supervisor = Sys.ActorOf(BackoffSupervisor.Props(options));

            supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
            // new instance
            var child = (await ExpectMsgAsync<BackoffSupervisor.CurrentChild>()).Ref;

            child.Tell("PING");
            await ExpectMsgAsync("PONG");

            supervisor.Tell("THROW");
            await ExpectMsgAsync("THROWN");

            child.Tell("PING");
            await ExpectNoMsgAsync(100.Milliseconds()); // Child is in limbo due to latch in postStop. There is no Terminated message yet

            supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
            (await ExpectMsgAsync<BackoffSupervisor.CurrentChild>()).Ref.Should().BeSameAs(child);

            supervisor.Tell(BackoffSupervisor.GetRestartCount.Instance);
            (await ExpectMsgAsync<BackoffSupervisor.RestartCount>()).Count.Should().Be(0);

            postStopLatch.CountDown();

            // New child is ready
            await AwaitAssertAsync(async () =>
            {
                supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
                // new instance
                (await ExpectMsgAsync<BackoffSupervisor.CurrentChild>()).Ref.Should().NotBeSameAs(child);
            });
        }

        [Fact]
        public async Task BackoffOnRestartSupervisor_must_respect_maxNrOfRetries_property_of_OneForOneStrategy()
        {
            var probe = CreateTestProbe();
            var supervisor = Sys.ActorOf(SupervisorProps(probe.Ref));
            await probe.ExpectMsgAsync("STARTED");

            await EventFilter.Exception<TestException>().ExpectAsync(5, async() =>
            {
                probe.Watch(supervisor);
                for (var i = 1; i <= 5; i++)
                {
                    supervisor.Tell("THROW");
                    if (i < 5)
                    {
                        // Since we should've died on this throw, don't expect to be started.
                        // We're not testing timing, so set a reasonably high timeout.
                        await probe.ExpectMsgAsync("STARTED", 4.Seconds());
                    }
                }

                await probe.ExpectTerminatedAsync(supervisor);
            });
        }

        [SkippableFact]
        public async Task BackoffOnRestartSupervisor_must_respect_withinTimeRange_property_of_OneForOneStrategy()
        {
            var probe = CreateTestProbe();
            // withinTimeRange indicates the time range in which maxNrOfRetries will cause the child to
            // stop. IE: If we restart more than maxNrOfRetries in a time range longer than withinTimeRange
            // that is acceptable.
            var options = Backoff.OnFailure(TestActor.Props(probe.Ref), "someChildName", 100.Milliseconds(), 10.Seconds(), 0.0, -1)
                .WithSupervisorStrategy(new OneForOneStrategy(3, 2.Seconds(), ex => ex is StoppingException 
                    ? Directive.Stop 
                    : SupervisorStrategy.DefaultStrategy.Decider.Decide(ex)));
            var supervisor = Sys.ActorOf(BackoffSupervisor.Props(options));

            supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
            await probe.ExpectMsgAsync("STARTED");

            probe.Watch(supervisor);
            // Throw three times rapidly
            for (var i = 1; i <= 3; i++)
            {
                supervisor.Tell("THROW");
                await probe.ExpectMsgAsync("STARTED");
            }

            // Now wait the length of our window, and throw again. We should still restart.
            await Task.Delay(2100);

            var stopwatch = Stopwatch.StartNew();
            // Throw three times rapidly
            for (var i = 1; i <= 3; i++)
            {
                supervisor.Tell("THROW");
                await probe.ExpectMsgAsync("STARTED");
            }
            stopwatch.Stop();
            Skip.If(stopwatch.ElapsedMilliseconds > 1500, "Could not satisfy test condition. Execution time exceeds the prescribed 2 seconds limit.");
            
            // Now we'll issue another request and should be terminated.
            supervisor.Tell("THROW");
            await probe.ExpectTerminatedAsync(supervisor);
        }
    }
}
