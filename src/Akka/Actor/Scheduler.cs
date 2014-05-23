using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Dispatch.SysMsg;

namespace Akka.Actor
{
    /// <summary>
    /// Class Scheduler.
    /// </summary>
    public class Scheduler
    {
        /// <summary>
        /// Schedules a message to a receiver once.
        /// </summary>
        /// <param name="initialDelay">The initial delay.</param>
        /// <param name="receiver">The receiver.</param>
        /// <param name="message">The message.</param>
        /// <returns>Task.</returns>
        public Task ScheduleOnce(TimeSpan initialDelay, ActorRef receiver, object message)
        {
            return ScheduleOnce(initialDelay, receiver, message, CancellationToken.None);
        }

        /// <summary>
        /// Schedules a message to a receiver once.
        /// </summary>
        /// <param name="initialDelay">The initial delay.</param>
        /// <param name="receiver">The receiver.</param>
        /// <param name="message">The message.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        public Task ScheduleOnce(TimeSpan initialDelay, ActorRef receiver, object message,
            CancellationToken cancellationToken)
        {
            return InternalScheduleOnce(initialDelay, () => receiver.Tell(message), cancellationToken);
        }

        /// <summary>
        /// Schedules a message to a receiver.
        /// </summary>
        /// <param name="initialDelay">The initial delay.</param>
        /// <param name="interval">The interval.</param>
        /// <param name="receiver">The receiver.</param>
        /// <param name="message">The message.</param>
        /// <returns>Task.</returns>
        public Task Schedule(TimeSpan initialDelay, TimeSpan interval, ActorRef receiver, object message)
        {
            return Schedule(initialDelay, interval, receiver, message, CancellationToken.None);
        }

        /// <summary>
        /// Schedules a message to a receiver.
        /// </summary>
        /// <param name="initialDelay">The initial delay.</param>
        /// <param name="interval">The interval.</param>
        /// <param name="receiver">The receiver.</param>
        /// <param name="message">The message.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        public Task Schedule(TimeSpan initialDelay, TimeSpan interval, ActorRef receiver, object message,
            CancellationToken cancellationToken)
        {
            return InternalSchedule(initialDelay, interval, () => receiver.Tell(message), cancellationToken);
        }

        //the action will be wrapped so that it completes inside the currently active actors mailbox if there is called from within an actor
        /// <summary>
        /// Schedules an Action.
        /// </summary>
        /// <param name="initialDelay">The initial delay.</param>
        /// <param name="interval">The interval.</param>
        /// <param name="action">The action.</param>
        /// <returns>Task.</returns>
        public Task Schedule(TimeSpan initialDelay, TimeSpan interval, Action action)
        {
            return Schedule(initialDelay, interval, action, CancellationToken.None);
        }

        /// <summary>
        /// Schedules an Action.
        /// </summary>
        /// <param name="initialDelay">The initial delay.</param>
        /// <param name="interval">The interval.</param>
        /// <param name="action">The action.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        public Task Schedule(TimeSpan initialDelay, TimeSpan interval, Action action,
            CancellationToken cancellationToken)
        {
            Action wrapped = WrapActionInActorSafeAction(action);
            return InternalSchedule(initialDelay, interval, wrapped, cancellationToken);
        }

        /// <summary>
        /// Schedules an Action once.
        /// </summary>
        /// <param name="initialDelay">The initial delay.</param>
        /// <param name="action">The action.</param>
        /// <returns>Task.</returns>
        public Task ScheduleOnce(TimeSpan initialDelay, Action action)
        {
            return ScheduleOnce(initialDelay, action, CancellationToken.None);
        }

        /// <summary>
        /// Schedules an Action once.
        /// </summary>
        /// <param name="initialDelay">The initial delay.</param>
        /// <param name="action">The action.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        public Task ScheduleOnce(TimeSpan initialDelay, Action action, CancellationToken cancellationToken)
        {
            Action wrapped = WrapActionInActorSafeAction(action);
            return InternalScheduleOnce(initialDelay, wrapped, cancellationToken);
        }

        /// <summary>
        /// Internals API for scheduling once.
        /// </summary>
        /// <param name="initialDelay">The initial delay.</param>
        /// <param name="action">The action.</param>
        /// <param name="token">The token.</param>
        /// <returns>Task.</returns>
        private async Task InternalScheduleOnce(TimeSpan initialDelay, Action action, CancellationToken token)
        {
            try
            {
                await Task.Delay(initialDelay, token);
            }
            catch (OperationCanceledException) { }
            if(!token.IsCancellationRequested)
                action();
        }

        /// <summary>
        /// Internal API for scheduling.
        /// </summary>
        /// <param name="initialDelay">The initial delay.</param>
        /// <param name="interval">The interval.</param>
        /// <param name="action">The action.</param>
        /// <param name="token">The token.</param>
        /// <returns>Task.</returns>
        private async Task InternalSchedule(TimeSpan initialDelay, TimeSpan interval, Action action,
            CancellationToken token)
        {
            await Task.Delay(initialDelay, token);
            while (!token.IsCancellationRequested)
            {
                action();
                try
                {
                    await Task.Delay(interval, token);
                }
                catch (TaskCanceledException) { }
                catch (OperationCanceledException) {}
            }
        }

        /// <summary>
        /// Wraps the action in an actor safe action.
        /// </summary>
        /// <param name="action">The action.</param>
        /// <returns>Action.</returns>
        private static Action WrapActionInActorSafeAction(Action action)
        {
            Action wrapped = action;
            if (ActorCell.Current != null)
            {
                LocalActorRef self = ActorCell.Current.Self;
                wrapped = () => self.Tell(new CompleteFuture(action));
            }
            return wrapped;
        }
    }
}