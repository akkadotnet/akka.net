//-----------------------------------------------------------------------
// <copyright file="TaskBasedScheduler_ActionScheduler_Cancellation_Tests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Tests.Actor.Scheduler
{
    // ReSharper disable once InconsistentNaming
    public class DefaultScheduler_ActionScheduler_Cancellation_Tests : AkkaSpec
    {
        [Fact]
        public void When_ScheduleOnce_using_canceled_Cancelable_Then_their_actions_should_not_be_invoked()
        {
            // Prepare, set up actions to be fired
            IActionScheduler scheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                var canceled = Cancelable.CreateCanceled();
                scheduler.ScheduleOnce(0, () => TestActor.Tell("Test"), canceled);

                //Validate that no messages were sent
                ExpectNoMsg(100);
            }
            finally
            {
                scheduler.AsInstanceOf<IDisposable>().Dispose();
            }
        }

        [Fact]
        public void When_ScheduleRepeatedly_using_canceled_Cancelable_Then_their_actions_should_not_be_invoked()
        {
            // Prepare, set up actions to be fired
            IActionScheduler scheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                var canceled = Cancelable.CreateCanceled();
                scheduler.ScheduleRepeatedly(0, 100, () => TestActor.Tell("Test1"), canceled);
                scheduler.ScheduleRepeatedly(50, 100, () => TestActor.Tell("Test2"), canceled);

                //Validate that no messages were sent
                ExpectNoMsg(150);
            }
            finally
            {
                scheduler.AsInstanceOf<IDisposable>().Dispose();
            }
        }

        [Fact]
        public void When_ScheduleOnce_and_then_canceling_before_they_occur_Then_their_actions_should_not_be_invoked()
        {
            // Prepare, set up actions to be fired
            IActionScheduler scheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                var cancelable = new Cancelable(scheduler);
                scheduler.ScheduleOnce(100, () => TestActor.Tell("Test"), cancelable);
                cancelable.Cancel();

                //Validate that no messages were sent
                ExpectNoMsg(150);
            }
            finally
            {
                scheduler.AsInstanceOf<IDisposable>().Dispose();
            }
        }

        [Fact]
        public void When_ScheduleRepeatedly_and_then_canceling_before_they_occur_Then_their_actions_should_not_be_invoked()
        {
            // Prepare, set up actions to be fired
            IActionScheduler scheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                var cancelable = new Cancelable(scheduler);
                scheduler.ScheduleRepeatedly(100, 2, () => TestActor.Tell("Test"), cancelable);
                cancelable.Cancel();

                //Validate that no messages were sent
                ExpectNoMsg(150);
            }
            finally
            {
                scheduler.AsInstanceOf<IDisposable>().Dispose();
            }
        }

        [Fact]
        public void When_canceling_existing_running_repeaters_Then_their_future_actions_should_not_be_invoked()
        {
            // Prepare, set up actions to be fired
            IActionScheduler scheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                var cancelable = new Cancelable(scheduler);
                scheduler.ScheduleRepeatedly(0, 150, () => TestActor.Tell("Test"), cancelable);
                ExpectMsg("Test");
                cancelable.Cancel();

                //Validate that no more messages were sent
                ExpectNoMsg(200);
            }
            finally
            {
                scheduler.AsInstanceOf<IDisposable>().Dispose();
            }
        }

        // Might be racy, failed at least once in Azure Pipelines.
        // Passed 500 consecutive local test runs with no fail with very heavy load without modification
        [Fact]
        public void When_canceling_existing_running_repeaters_by_scheduling_the_cancellation_ahead_of_time_Then_their_future_actions_should_not_be_invoked()
        {
            // Prepare, set up actions to be fired
            IActionScheduler scheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                var cancelableOdd = new Cancelable(scheduler);
                scheduler.ScheduleRepeatedly(1, 150, () => TestActor.Tell("Test"), cancelableOdd);
                cancelableOdd.CancelAfter(50);

                //Expect one message
                ExpectMsg("Test");

                //Validate that no messages were sent
                ExpectNoMsg(200);
            }
            finally
            {
                scheduler.AsInstanceOf<IDisposable>().Dispose();
            }
        }
    }
}

