//-----------------------------------------------------------------------
// <copyright file="TaskBasedScheduler_ActionScheduler_Schedule_Tests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using Akka.Actor;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;
using Xunit.Extensions;

namespace Akka.Tests.Actor.Scheduler
{
    // ReSharper disable once InconsistentNaming
    public class DefaultScheduler_ActionScheduler_Schedule_Tests : AkkaSpec
    {
        [Theory]
        [InlineData(10, 1000)]
        public void ScheduleRepeatedly_in_milliseconds_Tests_and_verify_the_interval(int initialDelay, int interval)
        {
            // Prepare, set up actions to be fired
            IActionScheduler scheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                var cancelable = new Cancelable(Sys.Scheduler);
                var receiver = ActorOf(dsl =>
                {
                    //Receive three messages, and store the time when these were received
                    //after three messages stop the actor and send the times to TestActor
                    var messages = new List<DateTimeOffset>();
                    dsl.Receive<string>((s, context) =>
                    {
                        messages.Add(context.System.Scheduler.Now);
                        if (messages.Count == 3)
                        {
                            TestActor.Tell(messages);
                            cancelable.Cancel();
                            context.Stop(context.Self);
                        }
                    });
                });
                scheduler.ScheduleRepeatedly(initialDelay, interval, () => receiver.Tell(""), cancelable);

                //Expect to get a list from receiver after it has received three messages
                var dateTimeOffsets = ExpectMsg<List<DateTimeOffset>>();
                dateTimeOffsets.ShouldHaveCount(3);
                Action<int, int> validate = (a, b) =>
                {
                    var valA = dateTimeOffsets[a];
                    var valB = dateTimeOffsets[b];
                    var diffBetweenMessages = Math.Abs((valB - valA).TotalMilliseconds);
                    var diffInMs = Math.Abs(diffBetweenMessages - interval);
                    var deviate = (diffInMs/interval);
                    deviate.Should(val => val < 0.1,
                        string.Format(
                            "Expected the interval between message {1} and {2} to deviate maximum 10% from {0}. It was {3} ms between the messages. It deviated {4}%",
                            interval, a + 1, b + 1, diffBetweenMessages, deviate*100));
                };
                validate(0, 1);
                validate(1, 2);
            }
            finally
            {
                scheduler.AsInstanceOf<IDisposable>().Dispose();
            }

        }


        [Theory]
        [InlineData(50, 50)]
        [InlineData(00, 50)]
        public void ScheduleRepeatedly_in_milliseconds_Tests(int initialDelay, int interval)
        {
            // Prepare, set up actions to be fired
            IActionScheduler testScheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                testScheduler.ScheduleRepeatedly(initialDelay, interval, () => TestActor.Tell("Test"));

                //Just check that we receives more than one message
                ExpectMsg("Test");
                ExpectMsg("Test");
                ExpectMsg("Test");
            }
            finally
            {
                testScheduler.AsInstanceOf<IDisposable>().Dispose();
            }
        }

        [Theory]
        [InlineData(50, 50)]
        [InlineData(00, 50)]
        public void ScheduleRepeatedly_in_TimeSpan_Tests(int initialDelay, int interval)
        {
            // Prepare, set up actions to be fired
            IActionScheduler testScheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                testScheduler.ScheduleRepeatedly(TimeSpan.FromMilliseconds(initialDelay),
                    TimeSpan.FromMilliseconds(interval), () => TestActor.Tell("Test"));

                //Just check that we receives more than one message
                ExpectMsg("Test");
                ExpectMsg("Test");
                ExpectMsg("Test");
            }
            finally
            {
                testScheduler.AsInstanceOf<IDisposable>().Dispose();
            }
        }


        [Fact]
        public void ScheduleOnceTests()
        {
            // Prepare, set up actions to be fired
            IActionScheduler testScheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                testScheduler.ScheduleOnce(50, () => TestActor.Tell("Test1"));
                testScheduler.ScheduleOnce(100, () => TestActor.Tell("Test2"));

                ExpectMsg("Test1");
                ExpectMsg("Test2");

                ExpectNoMsg(100);
            }
            finally
            {
                testScheduler.AsInstanceOf<IDisposable>().Dispose();
            }
        }



        [Theory]
        [InlineData(new int[] { 1, 1, 50, 50, 100, 100 })]
        public void When_ScheduleOnce_many_at_the_same_time_Then_all_fires(int[] times)
        {
            // Prepare, set up actions to be fired
            IActionScheduler scheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                foreach (var time in times)
                {
                    var t = time;
                    scheduler.ScheduleOnce(time, () => TestActor.Tell("Test" + t));
                }

                //Perform the test
                ExpectMsg("Test1");
                ExpectMsg("Test1");
                ExpectMsg("Test50");
                ExpectMsg("Test50");
                ExpectMsg("Test100");
                ExpectMsg("Test100");
                ExpectNoMsg(50);
            }
            finally
            {
                scheduler.AsInstanceOf<IDisposable>().Dispose();
            }
        }



        [Theory]
        [InlineData(-1)]
        [InlineData(-4711)]
        public void When_ScheduleOnce_with_invalid_delay_Then_exception_is_thrown(int invalidTime)
        {
            IActionScheduler testScheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);
            try
            {
                XAssert.Throws<ArgumentOutOfRangeException>(() =>
                            testScheduler.ScheduleOnce(invalidTime, () => { })
                );
            }
            finally
            {
                testScheduler.AsInstanceOf<IDisposable>().Dispose();
            }
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(-4711)]
        public void When_ScheduleRepeatedly_with_invalid_delay_Then_exception_is_thrown(int invalidTime)
        {
            IActionScheduler testScheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                XAssert.Throws<ArgumentOutOfRangeException>(() =>
                            testScheduler.ScheduleRepeatedly(invalidTime, 100, () => { })
                );
            }
            finally
            {
                testScheduler.AsInstanceOf<IDisposable>().Dispose();
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-4711)]
        public void When_ScheduleRepeatedly_with_invalid_interval_Then_exception_is_thrown(int invalidInterval)
        {
            IActionScheduler testScheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                XAssert.Throws<ArgumentOutOfRangeException>(() =>
                            testScheduler.ScheduleRepeatedly(42, invalidInterval, () => { })
                );
            }
            finally
            {
                testScheduler.AsInstanceOf<IDisposable>().Dispose();
            }
        }

        [Fact]
        public void When_ScheduleOnce_with_0_delay_Then_action_is_executed_immediately()
        {
            IActionScheduler testScheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                var manualResetEvent = new ManualResetEventSlim();
                manualResetEvent.IsSet.ShouldBeFalse();
                testScheduler.ScheduleOnce(0, () => manualResetEvent.Set());

                manualResetEvent.Wait(500).ShouldBeTrue();
            }
            finally
            {
                testScheduler.AsInstanceOf<IDisposable>().Dispose();
            }
        }

        [Fact]
        public void When_ScheduleRepeatedly_with_0_delay_Then_action_is_executed_immediately()
        {
            IActionScheduler testScheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                var manualResetEvent = new ManualResetEventSlim();
                manualResetEvent.IsSet.ShouldBeFalse();
                testScheduler.ScheduleRepeatedly(0, 100, () => manualResetEvent.Set());

                manualResetEvent.Wait(500).ShouldBeTrue();
            }
            finally
            {
                testScheduler.AsInstanceOf<IDisposable>().Dispose();
            }
        }

        [Fact]
        public void When_ScheduleRepeatedly_action_crashes_Then_no_more_calls_will_be_scheduled()
        {
            IActionScheduler testScheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                var timesCalled = 0;
                testScheduler.ScheduleRepeatedly(0, 10, () =>
                {
                    Interlocked.Increment(ref timesCalled);
                    throw new Exception("Crash");
                });
                AwaitCondition(() => timesCalled >= 1);
                Thread.Sleep(200); //Allow any scheduled actions to be fired. 

                //We expect only one of the scheduled actions to actually fire
                timesCalled.ShouldBe(1);
            }
            finally
            {
                testScheduler.AsInstanceOf<IDisposable>().Dispose();
            }
        }
    }
}

