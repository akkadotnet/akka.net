using System;

namespace Akka.Actor
{
    /// <summary>
    /// A scheduler able of scheduling actions
    /// </summary>
    public interface IActionScheduler
    {
        /// <summary>
        /// Schedules an action to be invoked after an delay.
        /// The action will be wrapped so that it completes inside the currently active actor if it is called from within an actor.
        /// <remarks>Note! It's considered bad practice to use concurrency inside actors, and very easy to get wrong so usage is discouraged.</remarks>
        /// </summary>
        /// <param name="delay">The time period that has to pass before the action is invoked.</param>
        /// <param name="action">The action to perform.</param>
        /// <param name="cancelable">A cancelable that can be used to cancel the action from being executed</param>
        void ScheduleOnce(TimeSpan delay, Action action, ICancelable cancelable);

        /// <summary>
        /// Schedules an action to be invoked after an delay.
        /// The action will be wrapped so that it completes inside the currently active actor if it is called from within an actor.
        /// <remarks>Note! It's considered bad practice to use concurrency inside actors, and very easy to get wrong so usage is discouraged.</remarks>
        /// </summary>
        /// <param name="delay">The time period that has to pass before the action is invoked.</param>
        /// <param name="action">The action to perform.</param>
        void ScheduleOnce(TimeSpan delay, Action action);

        /// <summary>
        /// Schedules an action to be invoked after an initial delay and then repeatedly.
        /// The action will be wrapped so that it completes inside the currently active actor if it is called from within an actor
        /// <remarks>Note! It's considered bad practice to use concurrency inside actors, and very easy to get wrong so usage is discouraged.</remarks>
        /// </summary>
        /// <param name="initialDelay">The time period that has to pass before first invocation.</param>
        /// <param name="interval">The interval, i.e. the time period that has to pass between the action is invoked.</param>
        /// <param name="action">The action to perform.</param>
        /// <param name="cancelable">A cancelable that can be used to cancel the action from being executed</param>
        void ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action, ICancelable cancelable);

        /// <summary>
        /// Schedules an action to be invoked after an initial delay and then repeatedly.
        /// The action will be wrapped so that it completes inside the currently active actor if it is called from within an actor
        /// <remarks>Note! It's considered bad practice to use concurrency inside actors, and very easy to get wrong so usage is discouraged.</remarks>
        /// </summary>
        /// <param name="initialDelay">The time period that has to pass before first invocation.</param>
        /// <param name="interval">The interval, i.e. the time period that has to pass between the action is invoked.</param>
        /// <param name="action">The action to perform.</param>
        void ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action);
    }
}