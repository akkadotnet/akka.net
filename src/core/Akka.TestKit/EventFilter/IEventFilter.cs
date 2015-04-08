using Akka.Event;

namespace Akka.TestKit
{
    // ReSharper disable once InconsistentNaming
    public interface IEventFilter
    {
        bool Apply(LogEvent logEvent);
    }
}