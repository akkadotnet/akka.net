using System;

namespace Akka.Actor
{
    public abstract class SchedulerBase : IScheduler, IAdvancedScheduler
    {
        void ITellScheduler.ScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, IActorRef sender)
        {
            ValidateDelay(delay, "delay");
            InternalScheduleTellOnce(delay, receiver, message, sender, null);
        }

        void ITellScheduler.ScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, IActorRef sender, ICancelable cancelable)
        {
            ValidateDelay(delay, "delay");
            InternalScheduleTellOnce(delay, receiver, message, sender, cancelable);

        }

        void ITellScheduler.ScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message, IActorRef sender)
        {
            ValidateDelay(initialDelay, "initialDelay");
            ValidateInterval(interval, "interval");
            InternalScheduleTellRepeatedly(initialDelay, interval, receiver, message, sender, null);
        }

        void ITellScheduler.ScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message, IActorRef sender, ICancelable cancelable)
        {
            ValidateDelay(initialDelay, "initialDelay");
            ValidateInterval(interval, "interval");
            InternalScheduleTellRepeatedly(initialDelay, interval, receiver, message, sender, cancelable);
        }


        void IActionScheduler.ScheduleOnce(TimeSpan delay, Action action)
        {
            ValidateDelay(delay, "delay");
            InternalScheduleOnce(delay, action, null);
        }

        void IActionScheduler.ScheduleOnce(TimeSpan delay, Action action, ICancelable cancelable)
        {
            ValidateDelay(delay, "delay");
            InternalScheduleOnce(delay, action, cancelable);
        }

        void IActionScheduler.ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action)
        {
            ValidateDelay(initialDelay, "initialDelay");
            ValidateInterval(interval, "interval");
            InternalScheduleRepeatedly(initialDelay, interval, action, null);
        }

        void IActionScheduler.ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action, ICancelable cancelable)
        {
            ValidateDelay(initialDelay, "initialDelay");
            ValidateInterval(interval, "interval");
            InternalScheduleRepeatedly(initialDelay, interval, action, cancelable);
        }

        IAdvancedScheduler IScheduler.Advanced { get { return this; } }

        DateTimeOffset ITimeProvider.Now { get { return TimeNow; } }

        protected abstract DateTimeOffset TimeNow { get; }

        protected abstract void InternalScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, IActorRef sender, ICancelable cancelable);

        protected abstract void InternalScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message, IActorRef sender, ICancelable cancelable);

        protected abstract void InternalScheduleOnce(TimeSpan delay, Action action, ICancelable cancelable);
        protected abstract void InternalScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action, ICancelable cancelable);


        protected static void ValidateInterval(TimeSpan interval, string parameterName)
        {
            if(interval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(parameterName, "Interval must be >0. It was " + interval);
        }

        protected static void ValidateDelay(TimeSpan delay, string parameterName)
        {
            if(delay < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(parameterName, "Delay must be >=0. It was " + delay);
        }
    }
}