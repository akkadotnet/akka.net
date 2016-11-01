﻿//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
            Run().Wait();
        }

        static async Task Run()
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

                var runnableGraph = source.Via(flow).ToMaterialized(sink, Keep.Right);
                await runnableGraph.Run(materializer);
            }
        }
    }
}
