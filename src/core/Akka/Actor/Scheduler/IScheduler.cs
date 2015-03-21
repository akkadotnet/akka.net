using System;

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