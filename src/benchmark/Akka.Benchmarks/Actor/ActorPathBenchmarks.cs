//-----------------------------------------------------------------------
// <copyright file="ActorPathBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Benchmarks.Configurations;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Actor
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class ActorPathBenchmarks
    {
        private ActorPath x;
        private ActorPath y;

        [GlobalSetup]
        public void Setup()
        {
            x = new RootActorPath(new Address("akka.tcp", "system", "127.0.0.1", 1337), "user");
            y = new RootActorPath(new Address("akka.tcp", "system", "127.0.0.1", 1337), "system");
        }

        [Benchmark]
        public ActorPath ActorPath_Parse()
        {
            return ActorPath.Parse("akka.tcp://system/user/parent/child");
        }

        [Benchmark]
        public ActorPath ActorPath_Concat()
        {
            return x / "parent" / "child";
        }
        
        [Benchmark]
        public bool ActorPath_Equals()
        {
            return x == y;
        }

        [Benchmark]
        public string ActorPath_ToString()
        {
            return x.ToString();
        }
    }
}
