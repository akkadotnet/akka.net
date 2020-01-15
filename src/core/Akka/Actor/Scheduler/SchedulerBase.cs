//-----------------------------------------------------------------------
// <copyright file="SchedulerBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.Event;

namespace Akka.Actor
{
    /// <summary>
    /// Abstract base class for implementing any custom <see cref="IScheduler"/> implementation used by Akka.NET.
    /// 
    /// All constructed schedulers are expected to support the <see cref="Config"/> and <see cref="ILoggingAdapter"/> arguments
    /// provided on the default constructor for this class.
    /// </summary>
    public abstract class SchedulerBase : IScheduler, IAdvancedScheduler
    {
        /// <summary>
        /// The configuration section for a specific <see cref="IScheduler"/> implementation.
        /// </summary>
        protected readonly Config SchedulerConfig;

        /// <summary>
        /// The <see cref="ILoggingAdapter"/> provided by the <see cref="ActorSystem"/> at startup.
        /// </summary>
        protected readonly ILoggingAdapter Log;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="scheduler">TBD</param>
        /// <param name="log">TBD</param>
        protected SchedulerBase(Config scheduler, ILoggingAdapter log)
        {
            SchedulerConfig = scheduler;
            Log = log;
        }

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

        /// <summary>
        /// TBD
        /// </summary>
        protected abstract DateTimeOffset TimeNow { get; }

        /// <summary>
        /// The current time since startup, as determined by the monotonic clock implementation.
        /// </summary>
        /// <remarks>
        /// Typically uses <see cref="MonotonicClock"/> in most implementations, but in some cases a 
        /// custom implementation is used - such as when we need to do virtual time scheduling in the Akka.TestKit.
        /// </remarks>
        public abstract TimeSpan MonotonicClock { get; }

        /// <summary>
        /// The current time since startup, as determined by the high resolution monotonic clock implementation.
        /// </summary>
        /// <remarks>
        /// Typically uses <see cref="MonotonicClock"/> in most implementations, but in some cases a 
        /// custom implementation is used - such as when we need to do virtual time scheduling in the Akka.TestKit.
        /// </remarks>
        public abstract TimeSpan HighResMonotonicClock { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="delay">TBD</param>
        /// <param name="receiver">TBD</param>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        /// <param name="cancelable">TBD</param>
        protected abstract void InternalScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, IActorRef sender, ICancelable cancelable);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="initialDelay">TBD</param>
        /// <param name="interval">TBD</param>
        /// <param name="receiver">TBD</param>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        /// <param name="cancelable">TBD</param>
        protected abstract void InternalScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message, IActorRef sender, ICancelable cancelable);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="delay">TBD</param>
        /// <param name="action">TBD</param>
        /// <param name="cancelable">TBD</param>
        protected abstract void InternalScheduleOnce(TimeSpan delay, Action action, ICancelable cancelable);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="initialDelay">TBD</param>
        /// <param name="interval">TBD</param>
        /// <param name="action">TBD</param>
        /// <param name="cancelable">TBD</param>
        protected abstract void InternalScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action, ICancelable cancelable);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="interval">TBD</param>
        /// <param name="parameterName">TBD</param>
        /// <exception cref="ArgumentOutOfRangeException">This exception is thrown if the given <paramref name="interval"/> is negative or zero.</exception>
        protected static void ValidateInterval(TimeSpan interval, string parameterName)
        {
            if(interval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(parameterName), $"Interval must be >0. It was {interval}");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="delay">TBD</param>
        /// <param name="parameterName">TBD</param>
        /// <exception cref="ArgumentOutOfRangeException">This exception is thrown if the given <paramref name="delay"/> is negative.</exception>
        protected static void ValidateDelay(TimeSpan delay, string parameterName)
        {
            if(delay < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(parameterName), $"Delay must be >=0. It was {delay}");
        }
    }
}

