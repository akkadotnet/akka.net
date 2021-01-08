//-----------------------------------------------------------------------
// <copyright file="SchedulerException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor
{
    /// <summary>
    /// An <see cref="AkkaException"/> that is thrown by the <see cref="IScheduler">Schedule*</see> methods
    /// when scheduling is not possible, e.g. after shutting down the <see cref="IScheduler"/>.
    /// </summary>
    public sealed class SchedulerException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SchedulerException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public SchedulerException(string message) : base(message) { }
    }
}

