//-----------------------------------------------------------------------
// <copyright file="FramingBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Streams;
using Akka.Streams.Dsl;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Streams
{
    /// <summary>
    /// Throughput and allocation benchmarks for <see cref="Framing"/> and <see cref="JsonFraming"/>
    /// stages. ROS&lt;byte&gt; variant (feature/spec1-ros-migration branch).
    /// </summary>
    [Config(typeof(ThroughputBenchmarkConfig))]
    public class FramingBenchmarks
    {
        private const int MessageCount = 100_000;
        // 16 MB cap; SimpleFramingProtocolDecoder adds 4 internally, so this stays clear of MaxFrameLength overflow.
        private const int MaxFrameLength = 16 * 1024 * 1024;

        [Params(64, 1024)]
        public int MessageSize { get; set; }

        private ActorSystem _system;
        private ActorMaterializer _materializer;

        private ReadOnlySequence<byte>[] _rawMessages;
        private ReadOnlySequence<byte>[] _delimiterFramedChunks;
        private ReadOnlySequence<byte>[] _jsonChunks;
        private ReadOnlySequence<byte> _delimiter;

        private TaskCompletionSource<NotUsed> _gate;
        private Task _completion;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _system = ActorSystem.Create("framing-bench", "akka.log-dead-letters = off");
            _materializer = _system.Materializer();

            var rng = new Random(42);
            _rawMessages = new ReadOnlySequence<byte>[MessageCount];
            for (var i = 0; i < MessageCount; i++)
            {
                var bytes = new byte[MessageSize];
                rng.NextBytes(bytes);
                _rawMessages[i] = new ReadOnlySequence<byte>(bytes);
            }

            _delimiter = new ReadOnlySequence<byte>(new byte[] { 0x00 });
            _delimiterFramedChunks = new ReadOnlySequence<byte>[MessageCount];
            for (var i = 0; i < MessageCount; i++)
            {
                var src = _rawMessages[i];
                var srcLen = (int)src.Length;
                var bytes = new byte[srcLen + 1];
                src.CopyTo(bytes.AsSpan(0, srcLen));
                for (var j = 0; j < srcLen; j++)
                    if (bytes[j] == 0x00) bytes[j] = 0xFF;
                bytes[srcLen] = 0x00;
                _delimiterFramedChunks[i] = new ReadOnlySequence<byte>(bytes);
            }

            BuildJsonChunks();
        }

        private void BuildJsonChunks()
        {
            var sb = new StringBuilder(MessageSize * MessageCount + 1024);
            var fillerLength = Math.Max(1, MessageSize - "{\"data\":\"\"}".Length);
            var filler = new string('x', fillerLength);
            for (var i = 0; i < MessageCount; i++)
            {
                sb.Append("{\"data\":\"").Append(filler).Append("\"}");
            }
            var allBytes = Encoding.UTF8.GetBytes(sb.ToString());

            var chunkSize = Math.Max(16, MessageSize / 3);
            var chunks = new System.Collections.Generic.List<ReadOnlySequence<byte>>(allBytes.Length / chunkSize + 1);
            for (var pos = 0; pos < allBytes.Length; pos += chunkSize)
            {
                var len = Math.Min(chunkSize, allBytes.Length - pos);
                chunks.Add(new ReadOnlySequence<byte>(allBytes, pos, len));
            }
            _jsonChunks = chunks.ToArray();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            _materializer?.Dispose();
            _system?.Dispose();
        }

        [IterationSetup(Target = nameof(LengthField_Encode))]
        public void SetupLengthFieldEncode()
        {
            _gate = new TaskCompletionSource<NotUsed>(TaskCreationOptions.RunContinuationsAsynchronously);
            _completion = Source.FromTask(_gate.Task)
                .ConcatMany(_ => Source.From(_rawMessages))
                .Via(Framing.SimpleFramingProtocolEncoder(MaxFrameLength))
                .RunWith(Sink.Ignore<ReadOnlySequence<byte>>(), _materializer);
        }

        [Benchmark(OperationsPerInvoke = MessageCount)]
        public Task LengthField_Encode()
        {
            _gate.SetResult(NotUsed.Instance);
            return _completion;
        }

        [IterationSetup(Target = nameof(LengthField_Decode))]
        public void SetupLengthFieldDecode()
        {
            _gate = new TaskCompletionSource<NotUsed>(TaskCreationOptions.RunContinuationsAsynchronously);
            _completion = Source.FromTask(_gate.Task)
                .ConcatMany(_ => Source.From(_rawMessages))
                .Via(Framing.SimpleFramingProtocolEncoder(MaxFrameLength))
                .Via(Framing.SimpleFramingProtocolDecoder(MaxFrameLength))
                .RunWith(Sink.Ignore<ReadOnlySequence<byte>>(), _materializer);
        }

        [Benchmark(OperationsPerInvoke = MessageCount)]
        public Task LengthField_Decode()
        {
            _gate.SetResult(NotUsed.Instance);
            return _completion;
        }

        [IterationSetup(Target = nameof(Delimiter_Decode))]
        public void SetupDelimiterDecode()
        {
            _gate = new TaskCompletionSource<NotUsed>(TaskCreationOptions.RunContinuationsAsynchronously);
            _completion = Source.FromTask(_gate.Task)
                .ConcatMany(_ => Source.From(_delimiterFramedChunks))
                .Via(Framing.Delimiter(_delimiter, MaxFrameLength))
                .RunWith(Sink.Ignore<ReadOnlySequence<byte>>(), _materializer);
        }

        [Benchmark(OperationsPerInvoke = MessageCount)]
        public Task Delimiter_Decode()
        {
            _gate.SetResult(NotUsed.Instance);
            return _completion;
        }

        [IterationSetup(Target = nameof(JsonFraming_MultiChunk))]
        public void SetupJsonFramingMultiChunk()
        {
            _gate = new TaskCompletionSource<NotUsed>(TaskCreationOptions.RunContinuationsAsynchronously);
            _completion = Source.FromTask(_gate.Task)
                .ConcatMany(_ => Source.From(_jsonChunks))
                .Via(JsonFraming.ObjectScanner(MaxFrameLength))
                .RunWith(Sink.Ignore<ReadOnlySequence<byte>>(), _materializer);
        }

        [Benchmark(OperationsPerInvoke = MessageCount)]
        public Task JsonFraming_MultiChunk()
        {
            _gate.SetResult(NotUsed.Instance);
            return _completion;
        }
    }
}
