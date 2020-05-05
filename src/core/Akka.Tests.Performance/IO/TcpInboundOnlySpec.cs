//-----------------------------------------------------------------------
// <copyright file="TcpInboundOnlySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.IO;
using Akka.Util;
using NBench;

namespace Akka.Tests.Performance.IO
{
    public class TcpInboundOnlySpec
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
            private int i = 0;
            public TestHandler(IActorRef connection, Counter inboundCounter, ManualResetEventSlim reset)
            {
                Context.Watch(connection);
                Receive<Tcp.Received>(received =>
                {
                    inboundCounter.Increment();
                    if ((++i) >= WriteCount)
                    {
                        reset.Set();
                        Context.Stop(Self);
                    }
                });
                Receive<Tcp.CommandFailed>(failed => Context.Stop(Self));
                Receive<Tcp.ConnectionClosed>(closed => Context.Stop(Self));
                Receive<Terminated>(terminated => Context.Stop(Self));
            }
        }

        const string InboundThroughputCounterName = "inbound ops";
        public static readonly IPEndPoint TestEndpoint = new IPEndPoint(IPAddress.Loopback, ThreadLocalRandom.Current.Next(5000, 11000));

        // The number of times we're going to warmup + run each benchmark
        public const int IterationCount = 3;
        public const int WriteCount = 300000;

        public const int MessagesPerMinute = 300000;
        public TimeSpan Timeout = TimeSpan.FromMinutes((double)WriteCount / MessagesPerMinute);
        
        private Counter inboundThroughputCounter;

        private ActorSystem system;
        private ManualResetEventSlim resetEvent = new ManualResetEventSlim(false);
        private CancellationTokenSource cancel = new CancellationTokenSource();

        private Socket clientSocket;
        private NetworkStream stream;
        private byte[] message;
        
        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            inboundThroughputCounter = context.GetCounter(InboundThroughputCounterName);
            var random = new Random();
            message = new byte[50];
            random.NextBytes(message);

            system = ActorSystem.Create("TcpInboundOnlySpec");
            system.ActorOf(Props.Create(() => new TestListener(TestEndpoint, inboundThroughputCounter, resetEvent)));
            
            clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            clientSocket.Connect(TestEndpoint);

            stream = new NetworkStream(clientSocket, true);
        }

        [PerfBenchmark(Description = "Measures how quickly and with how much GC overhead an actor server can receive 50B messages from client socket",
            NumberOfIterations = IterationCount, RunMode = RunMode.Iterations)]
        [CounterMeasurement(InboundThroughputCounterName)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        [MemoryMeasurement(MemoryMetric.TotalBytesAllocated)]
        public void TcpChannel_InboundOnly_Throughput(BenchmarkContext context)
        {
            Task.Run(async () =>
            {
                for (int i = 0; i < WriteCount; i++)
                {
                    await stream.WriteAsync(this.message, 0, this.message.Length, cancel.Token);
                    await stream.FlushAsync(cancel.Token);
                }
            }, cancel.Token);
            resetEvent.Wait(this.Timeout);
        }

        [PerfCleanup]
        public void TearDown()
        {
            try
            {
                cancel.Cancel();
                stream.Close();
            }
            finally
            {
                clientSocket.Dispose();
                system.Dispose();
            }
        }
    }
}
