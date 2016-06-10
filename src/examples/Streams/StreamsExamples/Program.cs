//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
                    .Select(x => x + 1);

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
