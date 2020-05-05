//-----------------------------------------------------------------------
// <copyright file="TcpHorizontalScaleSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.IO;
using Akka.Util;
using NBench;

namespace Akka.Tests.Performance.IO
{
    public class TcpHorizontalScaleSpec
    {
        private sealed class TestListener : ReceiveActor
        {
            public TestListener(EndPoint endPoint, Counter clientConnectCounter, Counter inboundCounter, Counter errorCounter)
            {
                Context.System.Tcp().Tell(new Tcp.Bind(Self, endPoint));
                Receive<Tcp.Connected>(connected =>
                {
                    var connection = Sender;
                    var handler = Context.ActorOf(Props.Create(() => new TestHandler(connection, inboundCounter, errorCounter)));
                    connection.Tell(new Tcp.Register(handler));
                    clientConnectCounter.Increment();
                });
            }
        }

        private sealed class TestHandler : ReceiveActor
        {
            private readonly Counter _errorCounter;

            public TestHandler(IActorRef connection, Counter inboundCounter, Counter errorCounter)
            {
                _errorCounter = errorCounter;
                connection.Tell(new Tcp.Register(Self));
                Context.Watch(connection);

                Receive<Tcp.Received>(received =>
                {
                    inboundCounter.Increment();
                    connection.Tell(Tcp.Write.Create(received.Data));
                });
                Receive<Tcp.ConnectionClosed>(closed =>
                {
                    if (closed.IsAborted || closed.IsErrorClosed)
                        errorCounter.Increment();

                    Context.Stop(Self);
                });
                Receive<Terminated>(terminated => Context.Stop(Self));
            }

            protected override void PreRestart(Exception reason, object message)
            {
                base.PreRestart(reason, message);
                _errorCounter.Increment();
            }
        }

        private sealed class TestClient : ReceiveActor
        {
            private readonly Counter _errorCounter;

            public TestClient(IPEndPoint remoteEndPoint, Counter outboundCounter, Counter errorCounter, byte[] payload)
            {
                Context.System.Tcp().Tell(new Tcp.Connect(remoteEndPoint));

                _errorCounter = errorCounter;
                IActorRef connection = Context.System.DeadLetters;
                Receive<Tcp.Received>(received =>
                {
                    outboundCounter.Increment();
                    connection.Tell(Tcp.Write.Create(received.Data));
                });
                Receive<Tcp.Connected>(connected =>
                {
                    connection = Sender;
                    Context.Watch(connection);
                    connection.Tell(new Tcp.Register(Self));
                    connection.Tell(Tcp.Write.Create(ByteString.FromBytes(payload)));
                });
                Receive<Tcp.CommandFailed>(failed =>
                {
                    _errorCounter.Increment();
                    Context.Stop(Self);
                });
                Receive<Tcp.ConnectionClosed>(closed =>
                {
                    if (closed.IsAborted || closed.IsErrorClosed) errorCounter.Increment();
                    Context.Stop(Self);
                });
                Receive<Terminated>(_ => Context.Stop(Self));
            }

            protected override void PreRestart(Exception reason, object message)
            {
                base.PreRestart(reason, message);
                _errorCounter.Increment();
            }
        }

        private const int IterationCount = 1;
        private const string ErrorCounterName = "exceptions caught";
        private const string InboundThroughputCounterName = "inbound ops";
        private const string OutboundThroughputCounterName = "outbound ops";
        private const string ClientConnectCounterName = "connected clients";

        private static readonly IPEndPoint TestEndPoint = new IPEndPoint(IPAddress.Loopback, ThreadLocalRandom.Current.Next(5000, 11000));
        private static readonly TimeSpan SleepInterval = TimeSpan.FromMilliseconds(100);

        private Counter _clientConnectedCounter;
        private Counter _inboundThroughputCounter;
        private Counter _outboundThroughputCounter;
        // we are counting errors on both sides - client and server
        private Counter _errorCounter;

        private ActorSystem _system;
        private IActorRef _listener;
        private ConcurrentBag<IActorRef> _clients;
        private byte[] _bytes = new byte[50];
        private const int WriteCount = 1000000;
        public const int MessagesPerMinute = 1000000;
        public TimeSpan Timeout = TimeSpan.FromMinutes((double)WriteCount / MessagesPerMinute);


        [PerfBenchmark(
            Description = "Measures how quickly and with how much GC overhead a Tcp server <-> Tcp client connection can passe 50B messages",
            NumberOfIterations = IterationCount, RunMode = RunMode.Iterations, SkipWarmups = true)]
        [CounterMeasurement(InboundThroughputCounterName)]
        [CounterMeasurement(OutboundThroughputCounterName)]
        [CounterMeasurement(ClientConnectCounterName)]
        [CounterMeasurement(ErrorCounterName)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        [MemoryMeasurement(MemoryMetric.TotalBytesAllocated)]
        public void Tcp_horizontal_scale_test()
        {
            Console.WriteLine($"Running benchmark for {Timeout}");
            var timeout = DateTime.UtcNow + Timeout;
            while (DateTime.UtcNow < timeout)
            {
                var client = _system.ActorOf(Props.Create(() => new TestClient(TestEndPoint, _outboundThroughputCounter, _errorCounter, _bytes)));
                _clients.Add(client);
                
                if(_clients.Count % 50 == 0)
                    Console.WriteLine($"Actual clients count: {_clients.Count}");
                Thread.Sleep(SleepInterval);
            }
        }

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            ThreadLocalRandom.Current.NextBytes(_bytes);
            _clients = new ConcurrentBag<IActorRef>();
            _clientConnectedCounter = context.GetCounter(ClientConnectCounterName);
            _inboundThroughputCounter = context.GetCounter(InboundThroughputCounterName);
            _outboundThroughputCounter = context.GetCounter(OutboundThroughputCounterName);
            _errorCounter = context.GetCounter(ErrorCounterName);

            var config = ConfigurationFactory.ParseString(@"
                akka {
                    loglevel=INFO
                    io.tcp.direct-buffer-pool {
                        buffer-size = 64
                        buffers-per-segment = 1000
                        buffer-pool-limit = 10000
                    }
                }");

            _system = ActorSystem.Create("TcpHorizontalScaleSpec", config);
            _listener = _system.ActorOf(Props.Create(() => new TestListener(TestEndPoint, _clientConnectedCounter, _inboundThroughputCounter, _errorCounter)), "listener");
            var client = _system.ActorOf(Props.Create(() => new TestClient(TestEndPoint, _outboundThroughputCounter, _errorCounter, _bytes)));
            _clients.Add(client);
        }

        [PerfCleanup]
        public void Cleanup()
        {
            foreach (var client in _clients)
            {
                _system.Stop(client);
            }
            _system.Stop(_listener);
            _system.Dispose();
        }
    }
}
