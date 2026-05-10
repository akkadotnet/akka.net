//-----------------------------------------------------------------------
// <copyright file="FramingBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.IO;
using Akka.Streams;
using Akka.Streams.Dsl;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Streams
{
    /// <summary>
    /// Throughput and allocation benchmarks for <see cref="Framing"/> and <see cref="JsonFraming"/>
    /// stages. Each benchmark materializes its graph in <see cref="IterationSetup"/> and parks it
    /// on a deferred source (<see cref="Source.FromTask{T}"/> waiting on a
    /// <see cref="TaskCompletionSource{T}"/>) so all materializer overhead — actor spawning,
    /// stage fusing, dispatcher attachment — is excluded from the measurement window. The
    /// benchmark method does only two things: trip the gate and await drain. With
    /// <c>OperationsPerInvoke = MessageCount</c>, the two fixed hops (gate signal + sink-complete)
    /// dilute to noise and the reported <c>Allocated</c> column is bytes-per-framed-message.
    /// </summary>
    [Config(typeof(ThroughputBenchmarkConfig))]
    public class FramingBenchmarks
    {
        private const int MessageCount = 100_000;
        // 16 MB cap on framed message length. SimpleFramingProtocolDecoder adds 4 internally,
        // so this stays well clear of int.MaxValue overflow (max + 4 ≈ 16 MB still).
        private const int MaxFrameLength = 16 * 1024 * 1024;

        [Params(64, 1024)]
        public int MessageSize { get; set; }

        private ActorSystem _system;
        private ActorMaterializer _materializer;

        // Reused across all iterations; rebuilt in GlobalSetup whenever MessageSize changes.
        private ByteString[] _rawMessages;
        private ByteString[] _delimiterFramedChunks;
        private ByteString[] _jsonChunks;
        private ByteString _delimiter;

        // Per-iteration state — fresh graph + gate per measurement.
        private TaskCompletionSource<NotUsed> _gate;
        private Task _completion;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _system = ActorSystem.Create("framing-bench", "akka.log-dead-letters = off");
            _materializer = _system.Materializer();

            // Random payload bytes give realistic compression-resistant data without
            // accidentally hitting any common-prefix optimization in the framing scan.
            var rng = new Random(42);
            _rawMessages = new ByteString[MessageCount];
            for (var i = 0; i < MessageCount; i++)
            {
                var bytes = new byte[MessageSize];
                rng.NextBytes(bytes);
                _rawMessages[i] = ByteString.FromBytes(bytes);
            }

            // Delimiter input: one message per chunk, terminated by '\n'. We avoid '\n' inside
            // the random payload by reserving byte 0x00 as the terminator instead.
            _delimiter = ByteString.FromBytes(new byte[] { 0x00 });
            _delimiterFramedChunks = new ByteString[MessageCount];
            for (var i = 0; i < MessageCount; i++)
            {
                // Replace any 0x00 in the payload to keep frames well-formed.
                var bytes = _rawMessages[i].ToArray();
                for (var j = 0; j < bytes.Length; j++)
                    if (bytes[j] == 0x00) bytes[j] = 0xFF;
                _delimiterFramedChunks[i] = ByteString.FromBytes(bytes) + _delimiter;
            }

            // JSON input: a stream of small objects split into chunks that *cross* object
            // boundaries, forcing the multi-Offer path inside JsonObjectParser. With chunk size
            // smaller than the object size, every Offer past the first one concatenates into
            // a non-empty buffer — which is exactly the path the migration optimized.
            BuildJsonChunks();
        }

        private void BuildJsonChunks()
        {
            // Build MessageCount JSON objects of ~MessageSize bytes each, concatenated with no
            // separator (the parser handles whitespace / commas itself). Split the resulting
            // stream into fixed-size chunks; chunk_size deliberately smaller than object_size
            // so every chunk straddles at least one object boundary.
            var sb = new StringBuilder(MessageSize * MessageCount + 1024);
            // {"data":"<filler>"}  — pad filler so total ≈ MessageSize bytes.
            var fillerLength = Math.Max(1, MessageSize - "{\"data\":\"\"}".Length);
            var filler = new string('x', fillerLength);
            for (var i = 0; i < MessageCount; i++)
            {
                sb.Append("{\"data\":\"").Append(filler).Append("\"}");
            }
            var allBytes = Encoding.UTF8.GetBytes(sb.ToString());

            // Chunk size = MessageSize / 3 → typically straddles object boundaries.
            var chunkSize = Math.Max(16, MessageSize / 3);
            var chunks = new System.Collections.Generic.List<ByteString>(allBytes.Length / chunkSize + 1);
            for (var pos = 0; pos < allBytes.Length; pos += chunkSize)
            {
                var len = Math.Min(chunkSize, allBytes.Length - pos);
                chunks.Add(ByteString.FromBytes(allBytes, pos, len));
            }
            _jsonChunks = chunks.ToArray();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            _materializer?.Dispose();
            _system?.Dispose();
        }

        // ----- LengthField encode -----

        [IterationSetup(Target = nameof(LengthField_Encode))]
        public void SetupLengthFieldEncode()
        {
            _gate = new TaskCompletionSource<NotUsed>(TaskCreationOptions.RunContinuationsAsynchronously);
            _completion = Source.FromTask(_gate.Task)
                .ConcatMany(_ => Source.From(_rawMessages))
                .Via(Framing.SimpleFramingProtocolEncoder(MaxFrameLength))
                .RunWith(Sink.Ignore<ByteString>(), _materializer);
        }

        [Benchmark(OperationsPerInvoke = MessageCount)]
        public Task LengthField_Encode()
        {
            _gate.SetResult(NotUsed.Instance);
            return _completion;
        }

        // ----- LengthField decode -----
        // Pre-encode the messages so the benchmark window only times the decode side.

        [IterationSetup(Target = nameof(LengthField_Decode))]
        public void SetupLengthFieldDecode()
        {
            _gate = new TaskCompletionSource<NotUsed>(TaskCreationOptions.RunContinuationsAsynchronously);
            _completion = Source.FromTask(_gate.Task)
                .ConcatMany(_ => Source.From(_rawMessages))
                .Via(Framing.SimpleFramingProtocolEncoder(MaxFrameLength))
                .Via(Framing.SimpleFramingProtocolDecoder(MaxFrameLength))
                .RunWith(Sink.Ignore<ByteString>(), _materializer);
        }

        [Benchmark(OperationsPerInvoke = MessageCount)]
        public Task LengthField_Decode()
        {
            _gate.SetResult(NotUsed.Instance);
            return _completion;
        }

        // ----- Delimiter decode -----

        [IterationSetup(Target = nameof(Delimiter_Decode))]
        public void SetupDelimiterDecode()
        {
            _gate = new TaskCompletionSource<NotUsed>(TaskCreationOptions.RunContinuationsAsynchronously);
            _completion = Source.FromTask(_gate.Task)
                .ConcatMany(_ => Source.From(_delimiterFramedChunks))
                .Via(Framing.Delimiter(_delimiter, MaxFrameLength))
                .RunWith(Sink.Ignore<ByteString>(), _materializer);
        }

        [Benchmark(OperationsPerInvoke = MessageCount)]
        public Task Delimiter_Decode()
        {
            _gate.SetResult(NotUsed.Instance);
            return _completion;
        }

        // ----- JSON multi-chunk -----
        // Drives JsonFraming.ObjectScanner with chunks that straddle object boundaries, so
        // most Offers concatenate into a non-empty buffer. OperationsPerInvoke is the number
        // of complete JSON objects emitted (= MessageCount), not the number of input chunks.

        [IterationSetup(Target = nameof(JsonFraming_MultiChunk))]
        public void SetupJsonFramingMultiChunk()
        {
            _gate = new TaskCompletionSource<NotUsed>(TaskCreationOptions.RunContinuationsAsynchronously);
            _completion = Source.FromTask(_gate.Task)
                .ConcatMany(_ => Source.From(_jsonChunks))
                .Via(JsonFraming.ObjectScanner(MaxFrameLength))
                .RunWith(Sink.Ignore<ByteString>(), _materializer);
        }

        [Benchmark(OperationsPerInvoke = MessageCount)]
        public Task JsonFraming_MultiChunk()
        {
            _gate.SetResult(NotUsed.Instance);
            return _completion;
        }
    }
}
