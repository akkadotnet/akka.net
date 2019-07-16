using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Util;

namespace Samples.StreamRefs
{
    class Program
    {
        public static Config GetHocon(string role, int port)
        {
            return ConfigurationFactory.ParseString(@"");
        }

        static async Task Main(string[] args)
        {
            var actorSystem = ActorSystem.Create("StreamRefs");

            var source = Source.Tick(TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(10), ThreadLocalRandom.Current.Next(0, 10000)).RunWith(Akka.Streams.Dsl.StreamRefs.SourceRef<int>(), 
                actorSystem.Materializer());
        }
    }
}
