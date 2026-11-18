//-----------------------------------------------------------------------
// <copyright file="TaskBasedScheduler_TellScheduler_Cancellation_Tests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;
using Xunit.Sdk;

namespace Akka.Tests.Actor.Scheduler
{
    // ReSharper disable once InconsistentNaming
    public class DefaultScheduler_TellScheduler_Cancellation_Tests : AkkaSpec
    {
        [Fact]
        public async Task When_ScheduleTellOnce_using_canceled_Cancelable_Then_their_actions_should_not_be_invoked()
        {
            // Prepare, set up actions to be fired
            ITellScheduler scheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                var canceled = Cancelable.CreateCanceled();
                scheduler.ScheduleTellOnce(0, TestActor, "Test", ActorRefs.NoSender, canceled);
                scheduler.ScheduleTellOnce(1, TestActor, "Test", ActorRefs.NoSender, canceled);

                //Validate that no messages were sent
                await ExpectNoMsgAsync(100);
            }
            finally
            {
                scheduler.AsInstanceOf<IDisposable>().Dispose();
            }
        }

        [Fact]
        public async Task When_ScheduleTellRepeatedly_using_canceled_Cancelable_Then_their_actions_should_not_be_invoked()
        {
            // Prepare, set up actions to be fired
            ITellScheduler scheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                var canceled = Cancelable.CreateCanceled();
                scheduler.ScheduleTellRepeatedly(0, 2, TestActor, "Test", ActorRefs.NoSender, canceled);
                scheduler.ScheduleTellRepeatedly(1, 2, TestActor, "Test", ActorRefs.NoSender, canceled);

                //Validate that no messages were sent
                await ExpectNoMsgAsync(100);
            }
            finally
            {
                scheduler.AsInstanceOf<IDisposable>().Dispose();
            }
        }

        [Fact]
        public async Task When_ScheduleTellOnce_and_then_canceling_before_they_occur_Then_their_actions_should_not_be_invoked()
        {
            // Prepare, set up actions to be fired
            IScheduler scheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                var cancelable = new Cancelable(scheduler);
                scheduler.ScheduleTellOnce(100, TestActor, "Test", ActorRefs.NoSender, cancelable);
                cancelable.Cancel();

                //Validate that no messages were sent
                await ExpectNoMsgAsync(150);
            }
            finally
            {
                scheduler.AsInstanceOf<IDisposable>().Dispose();
            }
        }


        [Fact]
        public async Task When_ScheduleTellRepeatedly_and_then_canceling_before_they_occur_Then_their_actions_should_not_be_invoked()
        {
            // Prepare, set up actions to be fired
            IScheduler scheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                var cancelable = new Cancelable(scheduler);
                scheduler.ScheduleTellRepeatedly(100, 2, TestActor, "Test", ActorRefs.NoSender, cancelable);
                cancelable.Cancel();

                //Validate that no messages were sent
                await ExpectNoMsgAsync(150);
            }
            finally
            {
                scheduler.AsInstanceOf<IDisposable>().Dispose();
            }
        }


        [Fact]
        public async Task When_canceling_existing_running_repeaters_Then_their_future_actions_should_not_be_invoked()
        {
            // Prepare, set up actions to be fired
            IScheduler scheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                var cancelable = new Cancelable(scheduler);
                var interval = TimeSpan.FromMilliseconds(150);
                scheduler.ScheduleTellRepeatedly(TimeSpan.Zero, interval, TestActor, "Test", ActorRefs.NoSender, cancelable);
                await ExpectMsgAsync("Test");
                cancelable.Cancel();

                // A tick that fired before Cancel() ran (for instance because this thread was
                // starved for longer than the interval) can legitimately sit in the TestActor's
                // queue. Drain those, then require the stream of ticks to stop: two intervals
                // of silence. If cancellation were broken, a tick would arrive every interval,
                // the silence would never happen and the deadline below would expire.
                await DrainInFlightTicksAsync(
                    quietPeriod: interval + interval,
                    deadline: TimeSpan.FromSeconds(5));
            }
            finally
            {
                scheduler.AsInstanceOf<IDisposable>().Dispose();
            }

        }

        private async Task DrainInFlightTicksAsync(TimeSpan quietPeriod, TimeSpan deadline)
        {
            var clock = Stopwatch.StartNew();
            while (await ReceiveOneAsync(quietPeriod) != null)
            {
                if (clock.Elapsed > deadline)
                    throw new XunitException(
                        $"Still receiving scheduled messages {clock.Elapsed} after the repeater was canceled.");
            }
        }

        [Fact]
        public async Task When_canceling_existing_running_repeaters_by_scheduling_the_cancellation_ahead_of_time_Then_their_future_actions_should_not_be_invoked()
        {
            // Prepare, set up actions to be fired
            IScheduler scheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                var cancelableOdd = new Cancelable(scheduler);
                scheduler.ScheduleTellRepeatedly(1, 150, TestActor, "Test", ActorRefs.NoSender, cancelableOdd);
                cancelableOdd.CancelAfter(50);

                //Expect one message
                await ExpectMsgAsync("Test");

                //Validate that no messages were sent
                await ExpectNoMsgAsync(200);
            }
            finally
            {
                scheduler.AsInstanceOf<IDisposable>().Dispose();
            }
        }

    }
}

