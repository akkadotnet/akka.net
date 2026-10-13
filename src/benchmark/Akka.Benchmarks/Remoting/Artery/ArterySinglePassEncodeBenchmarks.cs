//-----------------------------------------------------------------------
// <copyright file="ArterySinglePassEncodeBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using BenchmarkDotNet.Attributes;
using Codec = Akka.Remote.Artery.ArteryEnvelopeCodec;

namespace Akka.Benchmarks.Remoting.Artery
{
    /// <summary>
    /// Measures the production single-pass encoder, including serializer lookup and transport context.
    /// The reused byte-array payload isolates envelope allocations from payload construction.
    /// </summary>
    [Config(typeof(MicroBenchmarkConfig))]
    public class ArterySinglePassEncodeBenchmarks
    {
        private ActorSystem _system = null!;
        private byte[] _payload = null!;

        [Params(32, 256, 4096)]
        public int PayloadSize { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _system = ActorSystem.Create("single-pass-encode");
            _payload = new byte[PayloadSize];
        }

        [Benchmark]
        public int Encode()
        {
            using var writer = Codec.Encode(((ExtendedActorSystem)_system).Serialization, 42L,
                "akka://Sys@127.0.0.1:9001/user/sender", "akka://Sys@127.0.0.1:9002/user/recipient", _payload);
            return writer.WrittenCount;
        }

        [GlobalCleanup]
        public Task Cleanup() => _system.Terminate();
    }
}
