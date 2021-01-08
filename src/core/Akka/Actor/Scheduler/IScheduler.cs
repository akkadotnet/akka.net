//-----------------------------------------------------------------------
// <copyright file="IScheduler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor
{
    /// <summary>
    /// TBD
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

