using System.Collections.Generic;
using System.Linq;
using Akka.Actor;

namespace Akka.Streams.Dsl
{
    internal static class TestConfig
    {
        public static IEnumerable<int> RandomTestRange(ActorSystem system)
        {
            var numberOfTestsToRun = system.Settings.Config.GetInt("akka.stream.test.numberOfRandomizedTests", 10);
            return Enumerable.Range(1, numberOfTestsToRun);
        }
    }
}
