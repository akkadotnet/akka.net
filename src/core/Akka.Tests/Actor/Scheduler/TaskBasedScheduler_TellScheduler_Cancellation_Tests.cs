//-----------------------------------------------------------------------
// <copyright file="TaskBasedScheduler_TellScheduler_Cancellation_Tests.cs" company="Akka.NET Project">
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
    public class DefaultScheduler_TellScheduler_Cancellation_Tests : AkkaSpec
    {
        [Fact]
        public void When_ScheduleTellOnce_using_canceled_Cancelable_Then_their_actions_should_not_be_invoked()
        {
            // Prepare, set up actions to be fired
            ITellScheduler scheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                var canceled = Cancelable.CreateCanceled();
                scheduler.ScheduleTellOnce(0, TestActor, "Test", ActorRefs.NoSender, canceled);
                scheduler.ScheduleTellOnce(1, TestActor, "Test", ActorRefs.NoSender, canceled);

                //Validate that no messages were sent
                ExpectNoMsg(100);
            }
            finally
            {
                scheduler.AsInstanceOf<IDisposable>().Dispose();
            }
        }

        [Fact]
        public void When_ScheduleTellRepeatedly_using_canceled_Cancelable_Then_their_actions_should_not_be_invoked()
        {
            // Prepare, set up actions to be fired
            ITellScheduler scheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                var canceled = Cancelable.CreateCanceled();
                scheduler.ScheduleTellRepeatedly(0, 2, TestActor, "Test", ActorRefs.NoSender, canceled);
                scheduler.ScheduleTellRepeatedly(1, 2, TestActor, "Test", ActorRefs.NoSender, canceled);

                //Validate that no messages were sent
                ExpectNoMsg(100);
            }
            finally
            {
                scheduler.AsInstanceOf<IDisposable>().Dispose();
            }
        }

        [Fact]
        public void When_ScheduleTellOnce_and_then_canceling_before_they_occur_Then_their_actions_should_not_be_invoked()
        {
            // Prepare, set up actions to be fired
            IScheduler scheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                var cancelable = new Cancelable(scheduler);
                scheduler.ScheduleTellOnce(100, TestActor, "Test", ActorRefs.NoSender, cancelable);
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
        public void When_ScheduleTellRepeatedly_and_then_canceling_before_they_occur_Then_their_actions_should_not_be_invoked()
        {
            // Prepare, set up actions to be fired
            IScheduler scheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                var cancelable = new Cancelable(scheduler);
                scheduler.ScheduleTellRepeatedly(100, 2, TestActor, "Test", ActorRefs.NoSender, cancelable);
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
            IScheduler scheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                var cancelable = new Cancelable(scheduler);
                scheduler.ScheduleTellRepeatedly(0, 150, TestActor, "Test", ActorRefs.NoSender, cancelable);
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

        [Fact]
        public void When_canceling_existing_running_repeaters_by_scheduling_the_cancellation_ahead_of_time_Then_their_future_actions_should_not_be_invoked()
        {
            // Prepare, set up actions to be fired
            IScheduler scheduler = new HashedWheelTimerScheduler(Sys.Settings.Config, Log);

            try
            {
                var cancelableOdd = new Cancelable(scheduler);
                scheduler.ScheduleTellRepeatedly(1, 150, TestActor, "Test", ActorRefs.NoSender, cancelableOdd);
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

