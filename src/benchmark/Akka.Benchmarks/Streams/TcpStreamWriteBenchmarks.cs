//-----------------------------------------------------------------------
// <copyright file="TcpStreamWriteBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Buffers;
using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.IO;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.IO;
using BenchmarkDotNet.Attributes;
using Tcp = Akka.IO.Tcp;

namespace Akka.Benchmarks.Streams
{
    /// <summary>
    /// Isolates the Akka.Streams TCP <em>write</em> stage (<see cref="IncomingConnectionStage"/> /
    /// <c>TcpConnectionStage.TcpStreamLogic</c>) from real socket I/O by wiring it to a mock connection
    /// actor that acknowledges every <see cref="Tcp.Write"/> immediately. This measures the per-element
    /// stage-actor write-ack roundtrip cost — the dominant overhead the stream TCP write path adds on top
    /// of Akka.IO — without the noise of a live loopback socket.
    ///
    /// <para>
    /// The current implementation uses a strict credit-1 discipline: it sends one <c>Tcp.Write</c> with an
    /// ack request, then waits for the <c>WriteAck</c> before pulling the next element. These numbers form
    /// the baseline against which a pipelined / windowed write path can be compared and guarded.
    /// </para>
    /// </summary>
    [Config(typeof(ThroughputBenchmarkConfig))]
    public class TcpStreamWriteBenchmarks
    {
        private const int ElementCount = 100_000;

        [Params(128, 1024)]
        public int MessageSize { get; set; }

        /// <summary>
        /// Number of unacknowledged outbound writes allowed in flight to the (mock) connection actor.
        /// <c>1</c> is the historical strict credit-1 request/ack discipline and acts as the in-run baseline
        /// control; larger values pipeline writes to hide the per-write roundtrip latency.
        /// </summary>
        [Params(1, 2, 4, 8, 16, 32)]
        public int WriteWindow { get; set; }

        private ActorSystem _system;
        private ActorMaterializer _materializer;
        private IActorRef _ackingConnection;
        private EndPoint _remoteAddress;
        private ReadOnlySequence<byte>[] _messages;

        private TaskCompletionSource<NotUsed> _gate;
        private Task _completion;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _system = ActorSystem.Create("tcp-stream-write-bench", "akka.log-dead-letters = off");
            _materializer = _system.Materializer();
            _ackingConnection = _system.ActorOf(Props.Create(() => new AckingConnectionActor()), "acking-connection");
            _remoteAddress = new IPEndPoint(IPAddress.Loopback, 9999);

            _messages = new ReadOnlySequence<byte>[ElementCount];
            var rng = new System.Random(42);
            for (var i = 0; i < ElementCount; i++)
            {
                var bytes = new byte[MessageSize];
                rng.NextBytes(bytes);
                _messages[i] = new ReadOnlySequence<byte>(bytes);
            }
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            _materializer?.Dispose();
            _system?.Dispose();
        }

        [IterationSetup(Target = nameof(Tcp_WriteStage_Throughput))]
        public void Setup()
        {
            _gate = new TaskCompletionSource<NotUsed>(TaskCreationOptions.RunContinuationsAsynchronously);

            // halfClose:false so that upstream completion drives a Tcp.Close, which the mock answers with
            // Tcp.Closed, allowing the stage (and therefore the stream) to complete cleanly each iteration.
            var writeStage = new IncomingConnectionStage(_ackingConnection, _remoteAddress, halfClose: false, maxUnackedWrites: WriteWindow);

            _completion = Source.FromTask(_gate.Task)
                .ConcatMany(_ => Source.From(_messages))
                .Via(Flow.FromGraph(writeStage))
                .RunWith(Sink.Ignore<ReadOnlySequence<byte>>(), _materializer);
        }

        [Benchmark(OperationsPerInvoke = ElementCount)]
        public Task Tcp_WriteStage_Throughput()
        {
            _gate.SetResult(NotUsed.Instance);
            return _completion;
        }

        /// <summary>
        /// Stand-in for the Akka.IO <c>TcpConnection</c> actor. Acknowledges every write immediately
        /// (mirroring the real connection actor, which acks after admitting the write to its output pipe,
        /// not after socket drain) and answers close commands so the stream terminates deterministically.
        /// </summary>
        private sealed class AckingConnectionActor : ReceiveActor
        {
            public AckingConnectionActor()
            {
                Receive<Tcp.Write>(w =>
                {
                    if (w.WantsAck)
                        Sender.Tell(w.Ack);
                });
                Receive<Tcp.Close>(_ => Sender.Tell(Tcp.Closed.Instance));
                Receive<Tcp.ConfirmedClose>(_ => Sender.Tell(Tcp.ConfirmedClosed.Instance));
                Receive<Tcp.Abort>(_ => Sender.Tell(Tcp.Aborted.Instance));
                // Register / ResumeReading / SuspendReading: no-op for the write-path benchmark.
                Receive<Tcp.Register>(_ => { });
                Receive<Tcp.ResumeReading>(_ => { });
                Receive<Tcp.SuspendReading>(_ => { });
            }
        }
    }
}
