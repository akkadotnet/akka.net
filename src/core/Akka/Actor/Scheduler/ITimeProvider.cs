using System;

namespace Akka.Actor
{
    public interface ITimeProvider
    {
        /// <summary>
        /// Gets the scheduler's notion of current time.
        /// </summary>
        DateTimeOffset Now { get; }
    }
}