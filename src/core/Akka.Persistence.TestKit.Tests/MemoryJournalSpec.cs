using Akka.Persistence.TestKit.Journal;

namespace Akka.Persistence.TestKit.Tests
{
    public class MemoryJournalSpec : JournalSpec
    {
        public MemoryJournalSpec()
            : base(actorSystemName: "MemoryJournalSpec")
        {
        }
    }
}