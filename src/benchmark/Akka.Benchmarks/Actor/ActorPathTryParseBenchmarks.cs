//-----------------------------------------------------------------------
// <copyright file="ActorPathTryParseBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Benchmarks.Configurations;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using static Akka.Benchmarks.Configurations.BenchmarkCategories;

namespace Akka.Benchmarks.Actor
{
    [Config(typeof(MicroBenchmarkConfig))]
    [MemoryDiagnoser]
    public class ActorPathTryParseBenchmarks
    {
        private const string UserPath = "akka://sys@localhost:8081/user/a/b";
        private const string SystemPath = "akka://sys@localhost:8081/system/x";
        private const string TempPath = "akka://sys@localhost:8081/temp/abc";
        private const string OtherPath = "akka://sys@localhost:8081/other/a/b";

        [Benchmark]
        [BenchmarkCategory(MicroBenchmark, AkkaActorBenchmark)]
        public bool TryParse_User()
        {
            return ActorPath.TryParse(UserPath, out _);
        }

        [Benchmark]
        [BenchmarkCategory(MicroBenchmark, AkkaActorBenchmark)]
        public bool TryParse_System()
        {
            return ActorPath.TryParse(SystemPath, out _);
        }

        [Benchmark]
        [BenchmarkCategory(MicroBenchmark, AkkaActorBenchmark)]
        public bool TryParse_Temp()
        {
            return ActorPath.TryParse(TempPath, out _);
        }

        [Benchmark]
        [BenchmarkCategory(MicroBenchmark, AkkaActorBenchmark)]
        public bool TryParse_OtherControl()
        {
            return ActorPath.TryParse(OtherPath, out _);
        }
    }
}
