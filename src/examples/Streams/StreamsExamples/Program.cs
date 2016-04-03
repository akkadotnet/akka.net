using System;
using System.IO;
using System.Net;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using CsQuery;
using System.Linq;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.IO;

namespace StreamsExamples
{
    class Program
    {
        static void Main(string[] args)
        {
        }

        static async Task Run(Stream instream)
        {
            var oneSecond = TimeSpan.FromSeconds(1);

            using (var system = ActorSystem.Create("system"))
            using (var materializer = system.Materializer())
            {
                var source = Source.Tick(oneSecond, oneSecond, "hello");
                var flow = Flow.Create<string>()
                    .Select(s => s.ToUpper())
                    .Intersperse(", ");
                var sink = Sink.ForEach<string>(Console.WriteLine);

                var runnableGraph = source.Via(flow).To(sink);
                runnableGraph.Run(materializer);
            }
        }
    }
}
