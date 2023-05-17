//-----------------------------------------------------------------------
// <copyright file="BackoffSupervisorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Pattern;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using Akka.TestKit.Xunit2.Attributes;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

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

#pragma warning disable CS0162 // Disabled because without the return, the compiler complains about ambigious reference between Receive<T>(Action<T>,Predicate<T>) and Receive<T>(Predicate<T>,Action<T>)
            public Child(IActorRef probe)
            {
                _probe = probe;

                Receive<string>(str => str.Equals("boom"), _ =>
                {
                    throw new TestException();
                    return;
                });

                ReceiveAny(msg =>
                {
                    _probe.Tell(msg);
                });
            }
#pragma warning restore CS0162

            public static Props Props(IActorRef probe)
            {
                return Akka.Actor.Props.Create(() => new Child(probe));
            }
        }

        internal class ManualChild : ReceiveActor
        {
            private readonly IActorRef _probe;

#pragma warning disable CS0162 // Disabled because without the return, the compiler complains about ambigious reference between Receive<T>(Action<T>,Predicate<T>) and Receive<T>(Predicate<T>,Action<T>)
            public ManualChild(IActorRef probe)
            {
                _probe = probe;

                Receive<string>(str => str.Equals("boom"), _ =>
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
#pragma warning restore CS0162

            public static Props Props(IActorRef probe)
            {
                return Akka.Actor.Props.Create(() => new ManualChild(probe));
            }
        }

        private BackoffOptions OnStopOptions(int maxNrOfRetries = -1) => OnStopOptions(Child.Props(TestActor), maxNrOfRetries);
        private BackoffOptions OnStopOptions(Props props, int maxNrOfRetries = -1) => Backoff.OnStop(props, "c1", 100.Milliseconds(), 3.Seconds(), 0.2, maxNrOfRetries);
        private BackoffOptions OnFailureOptions(int maxNrOfRetries = -1) => OnFailureOptions(Child.Props(TestActor), maxNrOfRetries);
        private BackoffOptions OnFailureOptions(Props props, int maxNrOfRetries = -1) => Backoff.OnFailure(props, "c1", 100.Milliseconds(), 3.Seconds(), 0.2, maxNrOfRetries);
        private IActorRef Create(BackoffOptions options) => Sys.ActorOf(BackoffSupervisor.Props(options));
        #endregion

        [LocalFact(SkipLocal = "Racy on Azure DevOps")]
        public async Task BackoffSupervisor_must_start_child_again_when_it_stops_when_using_Backoff_OnStop()
        {
            var supervisor = Create(OnStopOptions());
            supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
            var c1 = (await ExpectMsgAsync<BackoffSupervisor.CurrentChild>()).Ref;
            Watch(c1);
            c1.Tell(PoisonPill.Instance);
            await ExpectTerminatedAsync(c1);
            await AwaitAssertAsync(async() =>
            {
                supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
                // new instance
                (await ExpectMsgAsync<BackoffSupervisor.CurrentChild>()).Ref.Should().NotBeSameAs(c1);
            });
        }

        [Fact]
        public async Task BackoffSupervisor_must_forward_messages_to_the_child()
        {
            Func<IActorRef, Task> assertForward = async supervisor =>
            {
                supervisor.Tell("hello");
                await ExpectMsgAsync("hello");
            };

            await assertForward(Create(OnStopOptions()));
            await assertForward(Create(OnFailureOptions()));
        }

        [Fact]
        public async Task BackoffSupervisor_must_support_custom_supervision_strategy()
        {
            Func<IActorRef, Task> assertCustomStrategy = async supervisor =>
            {
                supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
                var c1 = (await ExpectMsgAsync<BackoffSupervisor.CurrentChild>()).Ref;
                Watch(c1);
                c1.Tell("boom");
                await ExpectTerminatedAsync(c1);
                await AwaitAssertAsync(async () =>
                {
                    supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
                    // new instance
                    (await ExpectMsgAsync<BackoffSupervisor.CurrentChild>()).Ref.Should().NotBeSameAs(c1);
                });
            };

            // TODO: use FilterException
            await EventFilter.Exception<TestException>().ExpectAsync(2, async () =>
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

                await assertCustomStrategy(Create(OnStopOptions().WithSupervisorStrategy(stoppingStrategy)));
                await assertCustomStrategy(Create(OnFailureOptions().WithSupervisorStrategy(restartingStrategy)));
            });
        }

        [Fact]
        public async Task BackoffSupervisor_must_support_default_stopping_strategy_when_using_Backoff_OnStop()
        {
            // TODO: use FilterException
            await EventFilter.Exception<TestException>().ExpectAsync(1, async () =>
            {
                var supervisor = Create(OnStopOptions().WithDefaultStoppingStrategy().WithManualReset());
                supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
                var c1 = (await ExpectMsgAsync<BackoffSupervisor.CurrentChild>()).Ref;
                Watch(c1);
                supervisor.Tell(BackoffSupervisor.GetRestartCount.Instance);
                (await ExpectMsgAsync<BackoffSupervisor.RestartCount>()).Count.Should().Be(0);

                c1.Tell("boom");
                await ExpectTerminatedAsync(c1);
                await AwaitAssertAsync(async() =>
                {
                    supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
                    // new instance
                    (await ExpectMsgAsync<BackoffSupervisor.CurrentChild>()).Ref.Should().NotBeSameAs(c1);
                });
                supervisor.Tell(BackoffSupervisor.GetRestartCount.Instance);
                (await ExpectMsgAsync<BackoffSupervisor.RestartCount>()).Count.Should().Be(1);
            });
        }

        [Fact]
        public async Task BackoffSupervisor_must_support_manual_reset()
        {
            Func<IActorRef, Task> assertManualReset = async supervisor =>
            {
                supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
                var c1 = (await ExpectMsgAsync<BackoffSupervisor.CurrentChild>()).Ref;
                Watch(c1);
                c1.Tell("boom");
                await ExpectTerminatedAsync(c1);

                await AwaitAssertAsync(async() =>
                {
                    supervisor.Tell(BackoffSupervisor.GetRestartCount.Instance);
                    (await ExpectMsgAsync<BackoffSupervisor.RestartCount>()).Count.Should().Be(1);
                });

                await AwaitAssertAsync(async () =>
                {
                    supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
                    // new instance
                    (await ExpectMsgAsync<BackoffSupervisor.CurrentChild>()).Ref.Should().NotBeSameAs(c1);
                });

                // TODO: this Thread.Sleep should be removed
                await Task.Delay(500);

                supervisor.Tell("hello");
                await ExpectMsgAsync("hello");

                // making sure the Reset is handled by supervisor
                supervisor.Tell("hello");
                await ExpectMsgAsync("hello");

                supervisor.Tell(BackoffSupervisor.GetRestartCount.Instance);
                (await ExpectMsgAsync<BackoffSupervisor.RestartCount>()).Count.Should().Be(0);
            };

            // TODO: use FilterException
            await EventFilter.Exception<TestException>().ExpectAsync(2, async() =>
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

                await assertManualReset(
                    Create(OnStopOptions(ManualChild.Props(TestActor))
                        .WithManualReset()
                        .WithSupervisorStrategy(stoppingStrategy)));

                await assertManualReset(
                    Create(OnFailureOptions(ManualChild.Props(TestActor))
                        .WithManualReset()
                        .WithSupervisorStrategy(restartingStrategy)));
            });
        }

        [Fact]
        public async Task BackoffSupervisor_must_reply_to_sender_if_replyWhileStopped_is_specified()
        {
            await EventFilter.Exception<TestException>().ExpectAsync(1, async() =>
            {
                var supervisor = Create(Backoff.OnFailure(Child.Props(TestActor), "c1", TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(300), 0.2, -1)
                    .WithReplyWhileStopped("child was stopped"));
                supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);

                var c1 = (await ExpectMsgAsync<BackoffSupervisor.CurrentChild>()).Ref;
                Watch(c1);
                supervisor.Tell(BackoffSupervisor.GetRestartCount.Instance);
                (await ExpectMsgAsync<BackoffSupervisor.RestartCount>()).Count.Should().Be(0);

                c1.Tell("boom");
                await ExpectTerminatedAsync(c1);

                await AwaitAssertAsync(async() =>
                {
                    supervisor.Tell(BackoffSupervisor.GetRestartCount.Instance);
                    (await ExpectMsgAsync<BackoffSupervisor.RestartCount>()).Count.Should().Be(1);
                });

                supervisor.Tell("boom");
                await ExpectMsgAsync("child was stopped");
            });
        }

        [Fact]
        public async Task BackoffSupervisor_must_not_reply_to_sender_if_replyWhileStopped_is_not_specified()
        {
            await EventFilter.Exception<TestException>().ExpectAsync(1, async() =>
            {
                var supervisor = Create(Backoff.OnFailure(Child.Props(TestActor), "c1", TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(300), 0.2, -1));
                supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);

                var c1 = (await ExpectMsgAsync<BackoffSupervisor.CurrentChild>()).Ref;
                Watch(c1);
                supervisor.Tell(BackoffSupervisor.GetRestartCount.Instance);
                (await ExpectMsgAsync<BackoffSupervisor.RestartCount>()).Count.Should().Be(0);

                c1.Tell("boom");
                await ExpectTerminatedAsync(c1);

                await AwaitAssertAsync(async() =>
                {
                    supervisor.Tell(BackoffSupervisor.GetRestartCount.Instance);
                    (await ExpectMsgAsync<BackoffSupervisor.RestartCount>()).Count.Should().Be(1);
                });

                supervisor.Tell("boom"); //this will be sent to deadLetters
               await ExpectNoMsgAsync(500);
            });
        }

        [Theory, ClassData(typeof(DelayTable))]
        public void BackoffSupervisor_must_correctly_calculate_the_delay(int restartCount, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor, TimeSpan expectedResult)
        {
            Assert.Equal(expectedResult, BackoffSupervisor.CalculateDelay(restartCount, minBackoff, maxBackoff, randomFactor));
        }

        internal class DelayTable : IEnumerable<object[]>
        {
            private readonly List<object[]> delayTable = new List<object[]>
            {
                new object[] { 0, TimeSpan.Zero, TimeSpan.Zero, 0.0, TimeSpan.Zero },
                new object[] { 0, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(7), 0d, TimeSpan.FromMinutes(5) },
                new object[] { 2, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(7), 0d, TimeSpan.FromSeconds(7) },
                new object[] { 2, TimeSpan.FromSeconds(5), TimeSpan.FromDays(7), 0d, TimeSpan.FromSeconds(20) },
                new object[] { 29, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), 0d, TimeSpan.FromMinutes(10) },
                new object[] { 29, TimeSpan.FromDays(10000), TimeSpan.FromDays(10000), 0d, TimeSpan.FromDays(10000) },
                new object[] { int.MaxValue, TimeSpan.FromDays(10000), TimeSpan.FromDays(10000), 0d, TimeSpan.FromDays(10000) }
            };

            public IEnumerator<object[]> GetEnumerator() => delayTable.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [LocalFact(SkipLocal = "Racy on Azure DevOps")]
        public async Task BackoffSupervisor_must_stop_restarting_the_child_after_reaching_maxNrOfRetries_limit_using_BackOff_OnStop()
        {
            var supervisor = Create(OnStopOptions(maxNrOfRetries: 2));

            async Task<IActorRef> WaitForChild()
            {
                await AwaitConditionAsync(async() =>
                {
                    supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
                    var c = (await ExpectMsgAsync<BackoffSupervisor.CurrentChild>()).Ref;
                    return !c.IsNobody();
                }, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50));

                supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
                return (await ExpectMsgAsync<BackoffSupervisor.CurrentChild>()).Ref;
            }

            Watch(supervisor);

            supervisor.Tell(BackoffSupervisor.GetRestartCount.Instance);
            (await ExpectMsgAsync<BackoffSupervisor.RestartCount>()).Count.Should().Be(0);

            supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
            var c1 = (await ExpectMsgAsync<BackoffSupervisor.CurrentChild>()).Ref;
            Watch(c1);
            c1.Tell(PoisonPill.Instance);
            await ExpectTerminatedAsync(c1);

            supervisor.Tell(BackoffSupervisor.GetRestartCount.Instance);
            (await ExpectMsgAsync<BackoffSupervisor.RestartCount>()).Count.Should().Be(1);

            // This code looks suspicious, this might be the cause of the raciness
            var c2 = await WaitForChild();
            await AwaitAssertAsync(() => c2.ShouldNotBe(c1));
            Watch(c2);
            c2.Tell(PoisonPill.Instance);
            await ExpectTerminatedAsync(c2);

            supervisor.Tell(BackoffSupervisor.GetRestartCount.Instance);
            (await ExpectMsgAsync<BackoffSupervisor.RestartCount>()).Count.Should().Be(2);

            var c3 = await WaitForChild();
            await AwaitAssertAsync(() => c3.ShouldNotBe(c2));
            Watch(c3);
            c3.Tell(PoisonPill.Instance);
            await ExpectTerminatedAsync(c3);
            await ExpectTerminatedAsync(supervisor);
        }

        [LocalFact(SkipLocal = "Racy on Azure DevOps")]
        public async Task BackoffSupervisor_must_stop_restarting_the_child_after_reaching_maxNrOfRetries_limit_using_BackOff_OnFailure()
        {
            await EventFilter.Exception<TestException>().ExpectAsync(3, async() =>
            {
                var supervisor = Create(OnFailureOptions(maxNrOfRetries: 2));

                async Task<IActorRef> WaitForChild()
                {
                    await AwaitConditionAsync(async () =>
                    {
                        supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
                        var c = (await ExpectMsgAsync<BackoffSupervisor.CurrentChild>()).Ref;
                        return !c.IsNobody();
                    }, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50));

                    supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
                    return (await ExpectMsgAsync<BackoffSupervisor.CurrentChild>()).Ref;
                }

                Watch(supervisor);

                supervisor.Tell(BackoffSupervisor.GetRestartCount.Instance);
                (await ExpectMsgAsync<BackoffSupervisor.RestartCount>()).Count.Should().Be(0);

                supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
                var c1 = (await ExpectMsgAsync<BackoffSupervisor.CurrentChild>()).Ref;
                Watch(c1);
                c1.Tell("boom");
                await ExpectTerminatedAsync(c1);

                supervisor.Tell(BackoffSupervisor.GetRestartCount.Instance);
                (await ExpectMsgAsync<BackoffSupervisor.RestartCount>()).Count.Should().Be(1);

                // This code looks suspicious, this might be the cause of the raciness
                var c2 = await WaitForChild();
                await AwaitAssertAsync(() => c2.ShouldNotBe(c1));
                Watch(c2);
                c2.Tell("boom");
                await ExpectTerminatedAsync(c2);

                supervisor.Tell(BackoffSupervisor.GetRestartCount.Instance);
                (await ExpectMsgAsync<BackoffSupervisor.RestartCount>()).Count.Should().Be(2);

                var c3 = await WaitForChild();
                await AwaitAssertAsync(() => c3.ShouldNotBe(c2));
                Watch(c3);
                c3.Tell("boom");
                await ExpectTerminatedAsync(c3);
                await ExpectTerminatedAsync(supervisor);
            });
        }

        [Fact]
        public async Task BackoffSupervisor_must_stop_restarting_the_child_if_final_stop_message_received_using_BackOff_OnStop()
        {
            const string stopMessage = "stop";
            var supervisor = Create(OnStopOptions(maxNrOfRetries: 100).WithFinalStopMessage(message => ReferenceEquals(message, stopMessage)));
            supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
            var c1 = (await  ExpectMsgAsync<BackoffSupervisor.CurrentChild>()).Ref;
            var parentSupervisor = CreateTestProbe();
            Watch(c1);
            parentSupervisor.Watch(supervisor);

            supervisor.Tell(stopMessage);
            await ExpectMsgAsync("stop");
            c1.Tell(PoisonPill.Instance);
            await ExpectTerminatedAsync(c1);
            await parentSupervisor.ExpectTerminatedAsync(supervisor);
        }

        [Fact]
        public async Task BackoffSupervisor_must_not_stop_when_final_stop_message_has_not_been_received()
        {
            const string stopMessage = "stop";
            var supervisorWatcher = new TestProbe(Sys, new XunitAssertions());
            var supervisor = Create(OnStopOptions(maxNrOfRetries: 100).WithFinalStopMessage(message => ReferenceEquals(message, stopMessage)));
            supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
            var c1 = (await ExpectMsgAsync<BackoffSupervisor.CurrentChild>()).Ref;
            Watch(c1);
            supervisorWatcher.Watch(supervisor);

            c1.Tell(PoisonPill.Instance);
            await ExpectTerminatedAsync(c1);
            supervisor.Tell("ping");
            await supervisorWatcher.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(20)); // supervisor must not terminate

            supervisor.Tell(stopMessage);
            await supervisorWatcher.ExpectTerminatedAsync(supervisor);
        }
    }
}
