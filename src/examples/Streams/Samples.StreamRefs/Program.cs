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

            //var graph = GraphDsl.Create(sourceRef, (builder, shape) =>
            //{
            //    var graphSource = builder.Add(runHandle);
            //    var broadcast = builder.Add(new Broadcast<int>(2));

            //    builder.From(graphSource).Via(broadcast);
            //    builder.From(broadcast.Out(0)).To(shape);

            //    var countingFlow = Flow.Create<int>().Async().GroupedWithin(int.MaxValue, TimeSpan.FromSeconds(1));

            //    builder.From(broadcast.Out(1)).Via(countingFlow).To(Sink.ForEach<IEnumerable<int>>(i =>
            //    {
            //        var arr = i.ToArray();
            //        actorSystem.Log.Info("Emitted {0} events totaling {1}", arr.Length, arr.Sum());
            //    }));

            //    return ClosedShape.Instance;
            //});

            //var actorRef = await actorSystem.Materializer().Materialize(graph);

            var actorRef = await runHandle.RunWith(sourceRef, actorSystem.Materializer());

            await actorRef.Source.RunForeach(i =>
            {
                if (i % 5 == 0)
                {
                    actorSystem.Log.Info("{0}", i);
                }
            }, actorSystem.Materializer());
        }
    }
}
