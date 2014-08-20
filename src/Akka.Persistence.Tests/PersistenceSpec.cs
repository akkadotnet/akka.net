using Akka.TestKit;

namespace Akka.Persistence.Tests
{
    public abstract class PersistenceSpec : AkkaSpec
    {
        public string Name { get; set; }
    }
}