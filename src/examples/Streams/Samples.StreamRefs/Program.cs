using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Fusing;
using Akka.Util;

namespace Samples.StreamRefs
{
    /// <summary>
    /// Used to launch each of the different modules
    /// </summary>
    public interface IDemoActorSystem
    {
        Task<ActorSystem> Start(Config hocon);
    }

    class Program
    {
        public static Config GetHocon(string role, int port)
        {
            return ConfigurationFactory.ParseString(@"");
        }

        static async Task Main(string[] args)
        {
            var actorSystem = ActorSystem.Create("StreamRefs");

            // 
            var source = Source.Tick(TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(10),
                   1)
                .Select(x => ThreadLocalRandom.Current.Next(0, 1000))
                .PreMaterialize(actorSystem.Materializer());

            var cancelHandle = source.Item1;
            var runHandle = source.Item2;
            var sourceRef = Akka.Streams.Dsl.StreamRefs.SourceRef<int>();
            var sinkRef = Akka.Streams.Dsl.StreamRefs.SinkRef<int>();

            var sourceActorRef = await runHandle.RunWith(sourceRef, actorSystem.Materializer());
            var sinkActorRef = await Sink.ForEach<int>(i => Console.WriteLine(i)).RunWith(sinkRef, actorSystem.Materializer());

            var myFlow = Flow.Create<int>().Select(x => x + 1).Where(x => x % 2 == 0)
                .RunWith(sourceActorRef.Source, sinkActorRef.Sink, actorSystem.Materializer());



            await actorSystem.WhenTerminated;
        }
    }
}
