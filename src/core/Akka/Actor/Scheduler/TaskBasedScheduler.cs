using System;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Actor
{
    /// <summary>
    /// Class Scheduler.
    /// </summary>
    public class TaskBasedScheduler : SchedulerBase, IDateTimeOffsetNowTimeProvider
    {

        protected override DateTimeOffset TimeNow { get { return DateTimeOffset.Now; } }

        protected override void InternalScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, IActorRef sender, ICancelable cancelable)
        {
            var cancellationToken = cancelable == null ? CancellationToken.None : cancelable.Token;
            InternalScheduleOnce(delay, () => receiver.Tell(message, sender), cancellationToken);
        }

        protected override void InternalScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message, IActorRef sender, ICancelable cancelable)
        {
            var cancellationToken = cancelable == null ? CancellationToken.None : cancelable.Token;
            InternalScheduleRepeatedly(initialDelay, interval, () => receiver.Tell(message, sender), cancellationToken);
        }

        protected override void InternalScheduleOnce(TimeSpan delay, Action action, ICancelable cancelable)
        {
            var cancellationToken = cancelable == null ? CancellationToken.None : cancelable.Token;
            InternalScheduleOnce(delay, action, cancellationToken);
        }

        protected override void InternalScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action, ICancelable cancelable)
        {
            var cancellationToken = cancelable == null ? CancellationToken.None : cancelable.Token;
            InternalScheduleRepeatedly(initialDelay, interval, action, cancellationToken);
        }


        private void InternalScheduleOnce(TimeSpan initialDelay, Action action, CancellationToken token)
        {
            Task.Delay(initialDelay, token).ContinueWith(t =>
            {
                if(token.IsCancellationRequested) return;

                token.ThrowIfCancellationRequested();
                try
                {
                    action();
                }
                catch(OperationCanceledException e) { }
                //TODO: Should we log other exceptions? /@hcanber

            }, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Current);
        }


        private void InternalScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action, CancellationToken token)
        {
            Action<Task> executeAction = null;
            executeAction = t =>
            {
                if(token.IsCancellationRequested) return;
                try
                {
                    action();
                }
                catch(OperationCanceledException) { }
                //TODO: Should we log other exceptions? /@hcanber

                if(token.IsCancellationRequested) return;

                Task.Delay(interval, token)
                    .ContinueWith(executeAction, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Current);
            };
            Task.Delay(initialDelay, token)
                .ContinueWith(executeAction, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Current);

        }

    }
}