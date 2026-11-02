//-----------------------------------------------------------------------
// <copyright file="IScheduler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor
{
    /// <summary>
    /// Primary scheduler interface combining message-based scheduling via <see cref="ITellScheduler"/>
    /// and time access via <see cref="ITimeProvider"/>. Accessed through <see cref="ActorSystem.Scheduler"/>.
    /// </summary>
    public interface IScheduler : ITellScheduler, ITimeProvider
    {
        /// <summary>
        /// Gets the advanced scheduler which will allow you to schedule actions. 
        /// <remarks>Note! It's considered bad practice to use concurrency inside actors and very easy to get wrong so usage is discouraged.</remarks>
        /// </summary>
        IAdvancedScheduler Advanced { get; }
    }
}

