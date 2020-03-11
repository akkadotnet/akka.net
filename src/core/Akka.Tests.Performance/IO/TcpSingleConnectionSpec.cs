//-----------------------------------------------------------------------
// <copyright file="TcpSingleConnectionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.IO;
using Akka.Util;
using NBench;

namespace Akka.Tests.Performance.IO
{
    public class TcpSingleConnectionSpec
    {
        private sealed class TestListener : ReceiveActor
        {
            public TestListener(IPEndPoint endpoint, Counter inboundCounter, ManualResetEventSlim reset)
            {
                Context.System.Tcp().Tell(new Tcp.Bind(Self, endpoint));
                Receive<Tcp.Connected>(connected =>
                {
                    var connection = Sender;
                    var handler = Context.ActorOf(Props.Create(() => new TestHandler(connection, inboundCounter, reset)));
                    connection.Tell(new Tcp.Register(handler));
                });
            }
        }

        private sealed class TestHandler : ReceiveActor
        {
            public TestHandler(IActorRef connection, Counter inboundCounter, ManualResetEventSlim reset)
            {
                var i = 0;
                Context.Watch(connection);
                Receive<Tcp.Received>(received =>
                {
                    if ((++i) > WriteCount)
                    {
                        reset.Set();
                        Context.Stop(Self);
                    }
                    else
                    {
                        inboundCounter.Increment();
                        connection.Tell(Tcp.Write.Create(received.Data));
                    }
                });
                Receive<Tcp.ConnectionClosed>(closed => Context.Stop(Self));
                Receive<Terminated>(terminated => Context.Stop(Self));
            }
        }

        private sealed class TestClient : ReceiveActor
        {
            public TestClient(IPEndPoint remoteEndPoint, Counter outboundCounter, ManualResetEventSlim reset, TaskCompletionSource<int> completion)
            {
                Context.System.Tcp().Tell(new Tcp.Connect(remoteEndPoint));

                Receive<Tcp.CommandFailed>(failed => failed.Cmd is Tcp.Connect, failed =>
                {
                    reset.Set();
                    completion.SetCanceled();
                    Context.Stop(Self);
                });
                Receive<Tcp.Connected>(connected =>
                {
                    var connection = Sender;
                    Context.Watch(connection);
                    connection.Tell(new Tcp.Register(Self));
                    completion.SetResult(1);
                    Become(Connected(connection, outboundCounter, reset));
                });
            }

            private Action Connected(IActorRef connection, Counter counter, ManualResetEventSlim reset) => () =>
            {
                var i = 0;
                Receive<Tcp.Received>(received =>
                {
                    if ((++i) > WriteCount)
                    {
                        reset.Set();
                        Context.Stop(Self);
                    }
                    else
                    {
                        counter.Increment();
                        connection.Tell(Tcp.Write.Create(received.Data));
                    }
                });
                Receive<byte[]>(data =>
                {
                    connection.Tell(Tcp.Write.Create(ByteString.FromBytes(data)));
                });
                Receive<Tcp.CommandFailed>(failed =>
                {
                    reset.Set();
                    Context.Stop(Self);
                });
                Receive<Tcp.ConnectionClosed>(closed =>
                {
                    reset.Set();
                    Context.Stop(Self);
                });
            };
        }

        const string OutboundThroughputCounterName = "outbound ops";
        const string InboundThroughputCounterName = "inbound ops";
        public static readonly IPEndPoint TestEndpoint = new IPEndPoint(IPAddress.Loopback, ThreadLocalRandom.Current.Next(5000, 11000));

        // The number of times we're going to warmup + run each benchmark
        public const int IterationCount = 3;
        public const int WriteCount = 300000;

        public const int MessagesPerMinute = 300000;
        public TimeSpan Timeout = TimeSpan.FromMinutes((double)WriteCount / MessagesPerMinute);

        private Counter inboundThroughputCounter;
        private Counter outboundThroughputCounter;

        private ActorSystem system;
        private IActorRef client;
        private ManualResetEventSlim resetEvent = new ManualResetEventSlim(false);
        
        private byte[] message;

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            inboundThroughputCounter = context.GetCounter(InboundThroughputCounterName);
            outboundThroughputCounter = context.GetCounter(OutboundThroughputCounterName);
            var random = new Random();
            message = new byte[50];
            random.NextBytes(message);

            system = ActorSystem.Create("TcpSingleConnectionSpec");
            system.ActorOf(Props.Create(() => new TestListener(TestEndpoint, inboundThroughputCounter, resetEvent)));
            var completion = new TaskCompletionSource<int>();
            client = system.ActorOf(Props.Create(() => new TestClient(TestEndpoint, outboundThroughputCounter, resetEvent, completion)));
            completion.Task.Wait(Timeout);
        }

        [PerfBenchmark(Description = "Measures how quickly and with how much GC overhead a client/server actor connection can send 50B messages in both directions",
            NumberOfIterations = IterationCount, RunMode = RunMode.Iterations)]
        [CounterMeasurement(InboundThroughputCounterName)]
        [CounterMeasurement(OutboundThroughputCounterName)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        [MemoryMeasurement(MemoryMetric.TotalBytesAllocated)]
        public void Tcp_Duplex_Throughput(BenchmarkContext context)
        {
            client.Tell(message);
            resetEvent.Wait(this.Timeout);
        }

        [PerfCleanup]
        public void TearDown()
        {
            system.Terminate().Wait();
        }
    }
}
