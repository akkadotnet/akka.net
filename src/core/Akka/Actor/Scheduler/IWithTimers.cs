//-----------------------------------------------------------------------
// <copyright file="IWithTimers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor
{
    /// <summary>
    /// Marker interface for adding Timers support
    /// </summary>
    public interface IWithTimers
    {
        /// <summary>
        /// Gets or sets the TimerScheduler. This will be automatically populated by the framework in base constructor.
        /// Implement this as an auto property.
        /// </summary>
        ITimerScheduler Timers { get; set; }
    }
}
