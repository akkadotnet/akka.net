//-----------------------------------------------------------------------
// <copyright file="DeprecatedSchedulerExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    internal static class DeprecatedSchedulerExtensions
    {
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
    }
}

