//-----------------------------------------------------------------------
// <copyright file="DeprecatedSchedulerExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;

namespace Akka.Actor
{
    /// <summary>
    /// TBD
    /// </summary>
    [Obsolete("Deprecated. Will be removed [1.0.0]")]    //When removing this class, also make this constructor private:  internal CancellationTokenSourceCancelable(CancellationTokenSource source)
    public static class DeprecatedSchedulerExtensions
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="scheduler">TBD</param>
        /// <param name="initialDelay">TBD</param>
        /// <param name="receiver">TBD</param>
        /// <param name="message">TBD</param>
        [Obsolete("Use ScheduleTellOnce() or Context.SelfTellOnce() which will return an ICancelable. This method will be removed in future versions. [1.0.0]")]
        public static void ScheduleOnce(this IScheduler scheduler, TimeSpan initialDelay, IActorRef receiver, object message)
        {
            var sender = ActorCell.GetCurrentSelfOrNoSender();
            scheduler.Advanced.ScheduleOnce(initialDelay, () => receiver.Tell(message, sender), null);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="scheduler">TBD</param>
        /// <param name="initialDelay">TBD</param>
        /// <param name="receiver">TBD</param>
        /// <param name="message">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        [Obsolete("Use ScheduleTellOnce() or Context.SelfTellOnce() which will return an ICancelable. This method will be removed in future versions. [1.0.0]")]
        public static void ScheduleOnce(this IScheduler scheduler, TimeSpan initialDelay, IActorRef receiver, object message, CancellationToken cancellationToken)
        {
            var sender = ActorCell.GetCurrentSelfOrNoSender();
            scheduler.Advanced.ScheduleOnce(initialDelay, () => receiver.Tell(message, sender), null);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="scheduler">TBD</param>
        /// <param name="initialDelay">TBD</param>
        /// <param name="interval">TBD</param>
        /// <param name="receiver">TBD</param>
        /// <param name="message">TBD</param>
        [Obsolete("Use ScheduleTellRepeatedly() or Context.SelfTellRepeatedly() which will return an ICancelable. This method will be removed in future versions. [1.0.0]")]
        public static void Schedule(this IScheduler scheduler, TimeSpan initialDelay, TimeSpan interval, IActorRef receiver, object message)
        {
            var sender = ActorCell.GetCurrentSelfOrNoSender();
            scheduler.Advanced.ScheduleRepeatedly(initialDelay, interval, () => receiver.Tell(message, sender), null);
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="scheduler">TBD</param>
        /// <param name="initialDelay">TBD</param>
        /// <param name="interval">TBD</param>
        /// <param name="receiver">TBD</param>
        /// <param name="message">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        [Obsolete("Use ScheduleTellRepeatedly() or Context.SelfTellRepeatedly() instead. This method will be removed in future versions. [1.0.0]")]
        public static void Schedule(this IScheduler scheduler, TimeSpan initialDelay, TimeSpan interval, IActorRef receiver, object message, CancellationToken cancellationToken)
        {
            var sender = ActorCell.GetCurrentSelfOrNoSender();
            scheduler.Advanced.ScheduleRepeatedly(initialDelay, interval, () => receiver.Tell(message, sender), null);
        }



        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="scheduler">TBD</param>
        /// <param name="initialDelay">TBD</param>
        /// <param name="interval">TBD</param>
        /// <param name="action">TBD</param>
        [Obsolete("To schedule sending messages use ScheduleTellRepeatedly. Scheduling actions inside actors is discouraged, but if you really need to, use Advanced.ScheduleRepeatedly(). This method will be removed in future versions. [1.0.0]")]
        public static void Schedule(this IScheduler scheduler, TimeSpan initialDelay, TimeSpan interval, Action action)
        {
            scheduler.Advanced.ScheduleRepeatedly(initialDelay, interval, action, null);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="scheduler">TBD</param>
        /// <param name="initialDelay">TBD</param>
        /// <param name="interval">TBD</param>
        /// <param name="action">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        [Obsolete("To schedule sending messages use ScheduleTellRepeatedly. Scheduling actions inside actors is discouraged, but if you really need to, use Advanced.ScheduleRepeatedly(). This method will be removed in future versions. [1.0.0]")]
        public static void Schedule(this IScheduler scheduler, TimeSpan initialDelay, TimeSpan interval, Action action, CancellationToken cancellationToken)
        {
            var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var cancelable = new Cancelable(scheduler.Advanced, source);
            scheduler.Advanced.ScheduleRepeatedly(initialDelay, interval, action, cancelable);
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="scheduler">TBD</param>
        /// <param name="initialDelay">TBD</param>
        /// <param name="action">TBD</param>
        [Obsolete("To schedule sending messages use ScheduleTellOnce. Scheduling actions inside actors is discouraged, but if you really need to, use Advanced.ScheduleOnce(). This method will be removed in future versions. [1.0.0]")]
        public static void ScheduleOnce(this IScheduler scheduler, TimeSpan initialDelay, Action action)
        {
            scheduler.Advanced.ScheduleOnce(initialDelay, action, null);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="scheduler">TBD</param>
        /// <param name="initialDelay">TBD</param>
        /// <param name="action">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        [Obsolete("To schedule sending messages use ScheduleTellOnce. Scheduling actions inside actors is discouraged, but if you really need to, use Advanced.ScheduleOnce(). This method will be removed in future versions. [1.0.0]")]
        public static void ScheduleOnce(this IScheduler scheduler, TimeSpan initialDelay, Action action, CancellationToken cancellationToken)
        {
            var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var cancelable = new Cancelable(scheduler.Advanced, source);
            scheduler.Advanced.ScheduleOnce(initialDelay, action, cancelable);
        }
    }
}

