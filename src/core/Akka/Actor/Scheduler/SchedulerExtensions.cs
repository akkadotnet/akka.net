//-----------------------------------------------------------------------
// <copyright file="SchedulerExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor
{
    public static class SchedulerExtensions
    {
        /// <summary>Schedules to send a message once after a specified period of time.</summary>
        /// <param name="scheduler">The scheduler</param>
        /// <param name="millisecondsDelay">The time in milliseconds that has to pass before the message is sent.</param>
        /// <param name="receiver">The receiver.</param>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        /// <param name="cancelable">OPTIONAL. An <see cref="ICancelable"/> that can be used to cancel sending of the message. Note that once the message has been sent, it cannot be canceled.</param>
        public static void ScheduleTellOnce(this ITellScheduler scheduler, int millisecondsDelay, ICanTell receiver, object message, IActorRef sender, ICancelable cancelable = null)
        {
            scheduler.ScheduleTellOnce(TimeSpan.FromMilliseconds(millisecondsDelay), receiver, message, sender, cancelable);
        }


        /// <summary>Schedules to send a message repeatedly. The first message will be sent after the specified initial delay and there after at the rate specified.</summary>
        /// <param name="scheduler">The scheduler</param>
        /// <param name="initialMillisecondsDelay">The time in milliseconds that has to pass before the first message is sent.</param>
        /// <param name="millisecondsInterval">The interval, i.e. the time in milliseconds that has to pass between messages are being sent.</param>
        /// <param name="receiver">The receiver.</param>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        /// <param name="cancelable">OPTIONAL. An <see cref="ICancelable"/> that can be used to cancel sending of the message. Note that once the message has been sent, it cannot be canceled.</param>
        public static void ScheduleTellRepeatedly(this ITellScheduler scheduler, int initialMillisecondsDelay, int millisecondsInterval, ICanTell receiver, object message, IActorRef sender, ICancelable cancelable = null)
        {
            scheduler.ScheduleTellRepeatedly(TimeSpan.FromMilliseconds(initialMillisecondsDelay), TimeSpan.FromMilliseconds(millisecondsInterval), receiver, message, sender, cancelable);
        }

        /// <summary>
        /// Schedules an action to be invoked after an delay.
        /// The action will be wrapped so that it completes inside the currently active actor if it is called from within an actor.
        /// <remarks>Note! It's considered bad practice to use concurrency inside actors, and very easy to get wrong so usage is discouraged.</remarks>
        /// </summary>
        /// <param name="scheduler">The scheduler</param>
        /// <param name="millisecondsDelay">The time in milliseconds that has to pass before the action is invoked.</param>
        /// <param name="action">The action to perform.</param>
        /// <param name="cancelable">OPTIONAL. A cancelable that can be used to cancel the action from being executed. Defaults to <c>null</c></param>
        public static void ScheduleOnce(this IActionScheduler scheduler, int millisecondsDelay, Action action, ICancelable cancelable = null)
        {
            scheduler.ScheduleOnce(TimeSpan.FromMilliseconds(millisecondsDelay), action, cancelable);
        }

        /// <summary>
        /// Schedules an action to be invoked after an initial delay and then repeatedly.
        /// The action will be wrapped so that it completes inside the currently active actor if it is called from within an actor
        /// <remarks>Note! It's considered bad practice to use concurrency inside actors, and very easy to get wrong so usage is discouraged.</remarks>
        /// </summary>
        /// <param name="scheduler">The scheduler</param>
        /// <param name="initialMillisecondsDelay">The time in milliseconds that has to pass before first invocation.</param>
        /// <param name="millisecondsInterval">The interval, i.e. the time in milliseconds that has to pass before the action is invoked again.</param>
        /// <param name="action">The action to perform.</param>
        /// <param name="cancelable">OPTIONAL. A cancelable that can be used to cancel the action from being executed. Defaults to <c>null</c></param>
        public static void ScheduleRepeatedly(this IActionScheduler scheduler, int initialMillisecondsDelay, int millisecondsInterval, Action action, ICancelable cancelable = null)
        {
            scheduler.ScheduleRepeatedly(TimeSpan.FromMilliseconds(initialMillisecondsDelay), TimeSpan.FromMilliseconds(millisecondsInterval), action, cancelable);
        }



        /// <summary>Schedules to send a message once after a specified period of time.</summary>
        /// <param name="scheduler">The scheduler</param>
        /// <param name="delay">The time period that has to pass before the message is sent.</param>
        /// <param name="receiver">The receiver.</param>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        /// <returns>An <see cref="ICancelable"/> that can be used to cancel sending of the message. Once the message already has been sent, it cannot be cancelled.</returns>
        public static ICancelable ScheduleTellOnceCancelable(this IScheduler scheduler, TimeSpan delay, ICanTell receiver, object message, IActorRef sender)
        {
            var cancelable = new Cancelable(scheduler);
            scheduler.ScheduleTellOnce(delay, receiver, message, sender, cancelable);
            return cancelable;
        }

        /// <summary>Schedules to send a message once after a specified period of time.</summary>
        /// <param name="scheduler">The scheduler</param>
        /// <param name="millisecondsDelay">The time in milliseconds that has to pass before the message is sent.</param>
        /// <param name="receiver">The receiver.</param>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        /// <returns>An <see cref="ICancelable"/> that can be used to cancel sending of the message. Once the message already has been sent, it cannot be cancelled.</returns>
        public static ICancelable ScheduleTellOnceCancelable(this IScheduler scheduler, int millisecondsDelay, ICanTell receiver, object message, IActorRef sender)
        {
            var cancelable = new Cancelable(scheduler);
            scheduler.ScheduleTellOnce(millisecondsDelay, receiver, message, sender, cancelable);
            return cancelable;
        }

        /// <summary>Schedules to send a message repeatedly. The first message will be sent after the specified initial delay and there after at the rate specified.</summary>
        /// <param name="scheduler">The scheduler</param>
        /// <param name="initialDelay">The time period that has to pass before the first message is sent.</param>
        /// <param name="interval">The interval, i.e. the time period that has to pass between messages are being sent.</param>
        /// <param name="receiver">The receiver.</param>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        /// <returns>An <see cref="ICancelable"/> that can be used to cancel sending of the message. Once the message already has been sent, it cannot be cancelled.</returns>
        public static ICancelable ScheduleTellRepeatedlyCancelable(this IScheduler scheduler, TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message, IActorRef sender)
        {
            var cancelable = new Cancelable(scheduler);
            scheduler.ScheduleTellRepeatedly(initialDelay, interval, receiver, message, sender, cancelable);
            return cancelable;
        }

        /// <summary>Schedules to send a message repeatedly. The first message will be sent after the specified initial delay and there after at the rate specified.</summary>
        /// <param name="scheduler">The scheduler</param>
        /// <param name="initialMillisecondsDelay">The time in milliseconds that has to pass before the first message is sent.</param>
        /// <param name="millisecondsInterval">The interval, i.e. the time in milliseconds that has to pass between messages are sent.</param>
        /// <param name="receiver">The receiver.</param>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        /// <returns>An <see cref="ICancelable"/> that can be used to cancel sending of the message. Once the message already has been sent, it cannot be cancelled.</returns>
        public static ICancelable ScheduleTellRepeatedlyCancelable(this IScheduler scheduler, int initialMillisecondsDelay, int millisecondsInterval, ICanTell receiver, object message, IActorRef sender)
        {
            var cancelable = new Cancelable(scheduler);
            scheduler.ScheduleTellRepeatedly(initialMillisecondsDelay, millisecondsInterval, receiver, message, sender, cancelable);
            return cancelable;
        }



        /// <summary>
        /// Schedules an action to be invoked after an delay.
        /// </summary>
        /// <param name="scheduler">The scheduler</param>
        /// <param name="delay">The time period that has to pass before the action is invoked.</param>
        /// <param name="action">The action to perform.</param>
        /// <returns>A cancelable that can be used to cancel the action from being executed</returns>
        public static ICancelable ScheduleOnceCancelable(this IActionScheduler scheduler, TimeSpan delay, Action action)
        {
            var cancelable = new Cancelable(scheduler);
            scheduler.ScheduleOnce(delay, action, cancelable);
            return cancelable;
        }

        /// <summary>
        /// Schedules an action to be invoked after an delay.
        /// </summary>
        /// <param name="scheduler">The scheduler</param>
        /// <param name="millisecondsDelay">The time in milliseconds that has to pass before the action is invoked.</param>
        /// <param name="action">The action to perform.</param>
        /// <returns>A cancelable that can be used to cancel the action from being executed</returns>
        public static ICancelable ScheduleOnceCancelable(this IActionScheduler scheduler, int millisecondsDelay, Action action)
        {
            var cancelable = new Cancelable(scheduler);
            scheduler.ScheduleOnce(millisecondsDelay, action, cancelable);
            return cancelable;
        }



        /// <summary>
        /// Schedules an action to be invoked after an initial delay and then repeatedly.
        /// </summary>
        /// <param name="scheduler">The scheduler</param>
        /// <param name="initialDelay">The time period that has to pass before first invocation.</param>
        /// <param name="interval">The interval, i.e. the time period that has to pass between the action is invoked.</param>
        /// <param name="action">The action to perform.</param>
        /// <returns>A cancelable that can be used to cancel the action from being executed</returns>
        public static ICancelable ScheduleRepeatedlyCancelable(this IActionScheduler scheduler, TimeSpan initialDelay, TimeSpan interval, Action action)
        {
            var cancelable = new Cancelable(scheduler);
            scheduler.ScheduleRepeatedly(initialDelay, interval, action, cancelable);
            return cancelable;
        }

        /// <summary>
        /// Schedules an action to be invoked after an initial delay and then repeatedly.
        /// </summary>
        /// <param name="scheduler">The scheduler</param>
        /// <param name="initialMillisecondsDelay">The time in milliseconds that has to pass before first invocation.</param>
        /// <param name="millisecondsInterval">The interval, i.e. the time in milliseconds that has to pass between the action is invoked.</param>
        /// <param name="action">The action to perform.</param>
        /// <returns>A cancelable that can be used to cancel the action from being executed</returns>
        public static ICancelable ScheduleRepeatedlyCancelable(this IActionScheduler scheduler, int initialMillisecondsDelay, int millisecondsInterval, Action action)
        {
            var cancelable = new Cancelable(scheduler);
            scheduler.ScheduleRepeatedly(initialMillisecondsDelay, millisecondsInterval, action, cancelable);
            return cancelable;
        }


    }
}

