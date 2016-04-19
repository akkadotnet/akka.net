//-----------------------------------------------------------------------
// <copyright file="IScheduler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor
{
    public interface IScheduler : ITellScheduler, ITimeProvider
    {
        /// <summary>
        /// Gets the advanced scheduler which will allow you to schedule actions. 
        /// <remarks>Note! It's considered bad practice to use concurrency inside actors and very easy to get wrong so usage is discouraged.</remarks>
        /// </summary>
        IAdvancedScheduler Advanced { get; }
    }
}

