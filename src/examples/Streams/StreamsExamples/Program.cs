//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace StreamsExamples
{
    class Program
    {
        static void Main(string[] args)
        {
            Run();
            Console.ReadLine();
        }

        static void Run()
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

                var t = source.Via(flow).ToMaterialized(sink, Keep.Both).Run(materializer);
                var cancelTickSource = t.Item1;
                var foreachTask = t.Item2;

                foreachTask = foreachTask.ContinueWith(_ => Console.WriteLine("Foreach finished"));

                cancelTickSource.CancelAfter(TimeSpan.FromSeconds(5));

                foreachTask.Wait(TimeSpan.FromSeconds(10));

                Console.WriteLine("Task finished");
            }
        }
    }
}
