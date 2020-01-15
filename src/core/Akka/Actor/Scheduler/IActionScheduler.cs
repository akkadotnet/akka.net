//-----------------------------------------------------------------------
// <copyright file="IActionScheduler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor
{
    /// <summary>
    /// This interface defines a scheduler that is able to execute actions on a set schedule.
    /// </summary>
    public interface IActionScheduler
    {
        /// <summary>
        /// Schedules an action to be invoked after a delay. The action is wrapped so that it
        /// completes inside the currently active actor if it is called from within an actor.
        /// <remarks>Note! It's considered bad practice to use concurrency inside actors, and very easy to get wrong so usage is discouraged.</remarks>
        /// </summary>
        /// <param name="delay">The time period that has to pass before the action is invoked.</param>
        /// <param name="action">The action that is being scheduled.</param>
        /// <param name="cancelable">A cancelable used to cancel the action from being executed.</param>
        void ScheduleOnce(TimeSpan delay, Action action, ICancelable cancelable);

        /// <summary>
        /// Schedules an action to be invoked after a delay. The action is wrapped so that it
        /// completes inside the currently active actor if it is called from within an actor.
        /// <remarks>Note! It's considered bad practice to use concurrency inside actors, and very easy to get wrong so usage is discouraged.</remarks>
        /// </summary>
        /// <param name="delay">The time period that has to pass before the action is invoked.</param>
        /// <param name="action">The action that is being scheduled.</param>
        void ScheduleOnce(TimeSpan delay, Action action);

        /// <summary>
        /// Schedules an action to be invoked after an initial delay and then repeatedly.
        /// The action is wrapped so that it completes inside the currently active actor
        /// if it is called from within an actor.
        /// <remarks>Note! It's considered bad practice to use concurrency inside actors, and very easy to get wrong so usage is discouraged.</remarks>
        /// </summary>
        /// <param name="initialDelay">The time period that has to pass before first invocation of the action.</param>
        /// <param name="interval">The time period that has to pass between each invocation of the action.</param>
        /// <param name="action">The action that is being scheduled.</param>
        /// <param name="cancelable">A cancelable used to cancel the action from being executed.</param>
        void ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action, ICancelable cancelable);

        /// <summary>
        /// Schedules an action to be invoked after an initial delay and then repeatedly.
        /// The action is wrapped so that it completes inside the currently active actor
        /// if it is called from within an actor.
        /// <remarks>Note! It's considered bad practice to use concurrency inside actors, and very easy to get wrong so usage is discouraged.</remarks>
        /// </summary>
        /// <param name="initialDelay">The time period that has to pass before first invocation of the action.</param>
        /// <param name="interval">The time period that has to pass between each invocation of the action.</param>
        /// <param name="action">The action that is being scheduled.</param>
        void ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action);
    }
}

