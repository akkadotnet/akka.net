//-----------------------------------------------------------------------
// <copyright file="PrimitiveSerializerBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Configuration;
using Akka.Serialization;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Serialization
{
    /// <summary>
    /// Measures the primitive serializer (id 17) on the paths Artery and the byte[] callers use:
    /// <c>Serialize</c> into a reused <see cref="ArrayBufferWriter{T}"/>, <c>ToBinary</c>,
    /// <c>Deserialize</c> from a <see cref="ReadOnlySequence{T}"/> and <c>FromBinary</c>.
    /// The writer path resolves the serializer through <c>FindSerializerV2For</c>, the way Artery does,
    /// so it measures the V1 adapter while the serializer is V1 and the serializer itself once it is native.
    /// </summary>
    [Config(typeof(MicroBenchmarkConfig))]
    public class PrimitiveSerializerBenchmarks
    {
        private ExtendedActorSystem _system = null!;
        private SerializerV2 _v2 = null!;
        private Serializer _serializer = null!;
        private ArrayBufferWriter<byte> _writer = null!;
        private object _value = null!;
        private string _manifest = null!;
        private byte[] _bytes = null!;
        private ReadOnlySequence<byte> _sequence;

        [Params("String16", "String1K", "Int32", "Int64")]
        public string Kind { get; set; } = "String16";

        [GlobalSetup]
        public void Setup()
        {
            _system = (ExtendedActorSystem)ActorSystem.Create("primitive-bench",
                ConfigurationFactory.ParseString("akka.loglevel = OFF"));

            _value = Kind switch
            {
                "String16" => new string('a', 16),
                "String1K" => new string('a', 1024),
                "Int32" => int.MaxValue,
                "Int64" => long.MaxValue,
                _ => throw new InvalidOperationException($"Unknown kind [{Kind}]")
            };

            _v2 = _system.Serialization.FindSerializerV2For(_value);
            _serializer = _system.Serialization.FindSerializerFor(_value);
            _manifest = _serializer.Manifest(_value);
            _bytes = _serializer.ToBinary(_value);
            _sequence = new ReadOnlySequence<byte>(_bytes);
            _writer = new ArrayBufferWriter<byte>(2048);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _system.Dispose();
        }

        [Benchmark]
        public int SerializeToWriter()
        {
            _writer.ResetWrittenCount();
            return _v2.Serialize(_value, _writer);
        }

        [Benchmark]
        public byte[] ToBinary() => _serializer.ToBinary(_value);

        [Benchmark]
        public object DeserializeFromSequence() => _v2.Deserialize(_sequence, _manifest);

        [Benchmark]
        public object FromBinary() => _serializer.FromBinary(_bytes, _manifest);
    }
}
