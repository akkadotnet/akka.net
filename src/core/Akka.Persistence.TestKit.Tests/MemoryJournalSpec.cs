using Akka.Persistence.TestKit.Journal;

namespace Akka.Persistence.TestKit.Tests
{
    public class MemoryJournalSpec : JournalPerformanceSpec
    {
        public MemoryJournalSpec()
            : base(actorSystemName: "MemoryJournalSpec")
        {
        }
    }
}