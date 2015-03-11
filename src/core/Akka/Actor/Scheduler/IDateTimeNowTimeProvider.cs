using System;

namespace Akka.Actor
{
    /// <summary>
    /// Marks that an <see cref="ITimeProvider"/> uses <see cref="DateTimeOffset.UtcNow"/>, 
    /// i.e. system time, to provide <see cref="ITimeProvider.Now"/>.
    /// </summary>
    public interface IDateTimeOffsetNowTimeProvider : ITimeProvider
    { }
}