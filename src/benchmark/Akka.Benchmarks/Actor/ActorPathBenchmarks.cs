//-----------------------------------------------------------------------
// <copyright file="ActorPathBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using BenchmarkDotNet.Attributes;
using static Akka.Benchmarks.Configurations.BenchmarkCategories;
using BenchmarkDotNet.Configs;

namespace Akka.Benchmarks.Actor
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class ActorPathBenchmarks
    {
        private ActorPath _x;
        private ActorPath _y;
        private ActorPath _childPath;
        private readonly Address _sysAdr = new("akka.tcp", "system", "127.0.0.1", 1337);
        private readonly Address _otherAdr = new("akka.tcp", "system", "127.0.0.1", 1338);

        private string _actorPathStr;
        private ActorPath _mediumPath;
        private ActorPath _complexPath;

        private const string HashCodeCategory = "ActorPathHashCodeBenchmark";

        public int Uid { get; set; } = 10000;

        [GlobalSetup]
        public void Setup()
        {
            _x = new RootActorPath(_sysAdr, "user");
            _y = new RootActorPath(_sysAdr, "system");
            var parentPath = _x / "parent";
            _childPath = new ChildActorPath(parentPath, "child", Uid);
            _actorPathStr = _childPath.ToSerializationFormat();

            // Medium complexity: /user/parent/child/grandchild
            _mediumPath = _x / "parent" / "child" / "grandchild";

            // Complex: deeply nested, long names
            var complex = _x;
            for (int i = 0; i < 20; i++)
            {
                complex /= ("segment_" + i.ToString("D2") + new string('x', 10));
            }
            _complexPath = complex;
        }

        [Benchmark]
        [BenchmarkCategory(MicroBenchmark, AkkaActorBenchmark)]
        public ActorPath ActorPath_Parse()
        {
            return ActorPath.Parse(_actorPathStr);
        }

        [Benchmark]
        [BenchmarkCategory(MicroBenchmark, AkkaActorBenchmark)]
        public ActorPath ActorPath_Concat()
        {
            return _x / "parent" / "child";
        }
        
        [Benchmark]
        [BenchmarkCategory(MicroBenchmark, AkkaActorBenchmark)]
        public bool ActorPath_Equals()
        {
            return _x == _y;
        }

        [Benchmark]
        [BenchmarkCategory(MicroBenchmark, AkkaActorBenchmark)]
        public string ActorPath_ToString()
        {
            return _childPath.ToString();
        }

        [Benchmark]
        [BenchmarkCategory(MicroBenchmark, AkkaActorBenchmark)]
        public string ActorPath_ToSerializationFormat()
        {
            return _childPath.ToSerializationFormat();
        }

        [Benchmark]
        [BenchmarkCategory(MicroBenchmark, AkkaActorBenchmark)]
        public string ActorPath_ToSerializationFormatWithAddress()
        {
            return _childPath.ToSerializationFormatWithAddress(_otherAdr);
        }

        [Benchmark(Baseline = true)]
        [BenchmarkCategory(HashCodeCategory)]
        public int ActorPath_GetHashCode_Baseline()
        {
            // Simple root path
            return _x.GetHashCode();
        }

        [Benchmark]
        [BenchmarkCategory(HashCodeCategory)]
        public int ActorPath_GetHashCode_Medium()
        {
            // Medium complexity path
            return _mediumPath.GetHashCode();
        }

        [Benchmark]
        [BenchmarkCategory(HashCodeCategory)]
        public int ActorPath_GetHashCode_Complex()
        {
            // Deeply nested, long names
            return _complexPath.GetHashCode();
        }
    }
}
