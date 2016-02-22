using System;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;

namespace StreamsExamples
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var system = ActorSystem.Create("system"))
            using (var materializer = system.Materializer())
            {
                var flow = Source
                    .From(new[] {1, 2, 3})
                    .Map(x => x + 1);

                var result = flow
                    .RunForeach(x =>
                    {
                        Console.WriteLine(x);
                    }, materializer);

                result.Wait();
            }
        }
    }
}
