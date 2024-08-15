// -----------------------------------------------------------------------
//  <copyright file="Program.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Runtime;
using Akka.Actor;
using Akka.Configuration;

namespace SpawnBenchmark;

internal class Program
{
    private static void Main(string[] args)
    {
        Console.WriteLine($"Is Server GC {GCSettings.IsServerGC}");

        var config = ConfigurationFactory.ParseString("akka.suppress-json-serializer-warning=on");
        using (var sys = ActorSystem.Create("main", config))
        {
            var actor = sys.ActorOf(RootActor.Props);
            actor.Tell(new RootActor.Run(5));
            Console.ReadLine();
        }
    }
}