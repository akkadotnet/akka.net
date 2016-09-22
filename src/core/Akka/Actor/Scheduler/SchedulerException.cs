//-----------------------------------------------------------------------
// <copyright file="SchedulerException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor
{
    /// <summary>
    /// An <see cref="AkkaException"/> that is thrown by the <see cref="IScheduler.Schedule*"/> methods
    /// when scheduling is not possible, e.g. after shutting down the <see cref="IScheduler"/>.
    /// </summary>
    public sealed class SchedulerException : AkkaException
    {
        public SchedulerException(string message) : base(message) { }
    }
}

