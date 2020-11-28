using System;

namespace Akka.Persistence.Linq2Db.CompatibilityTests
{
    public class SomeEvent
    {
        public string EventName { get; set; }
        public int Number { get; set; }
        public Guid Guid { get; set; }
    }
}