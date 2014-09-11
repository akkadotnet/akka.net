using Akka.Event;

namespace Akka.TestKit
{
    // ReSharper disable once InconsistentNaming
    public interface EventFilter
    {
        bool Apply(LogEvent logEvent);
    }
}