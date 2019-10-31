// //-----------------------------------------------------------------------
// // <copyright file="TcpOperationsBenchmarks.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Event;
using Akka.IO;
using Akka.Util.Internal;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Attributes.Jobs;
using BenchmarkDotNet.Engines;

namespace Akka.Benchmarks
{
    [Config(typeof(MicroBenchmarkConfig))]
    [SimpleJob(warmupCount: 1, invocationCount: 1, launchCount: 1, runStrategy: RunStrategy.Monitoring, targetCount: 100)]
    public class TcpOperationsBenchmarks
    {
        private ActorSystem _system;
        private byte[] _message;
        private IActorRef _server;
        private IActorRef _client;

        [Params(100, 1000)]
        public int MessageCount { get; set; }
        [Params(10, 100)]
        public int MessageLength { get; set; }
        
        [GlobalSetup]
        public void Setup()
        {
            _system = ActorSystem.Create("system");
            _message = new byte[MessageLength];

            var port = new Random().Next(18000, 19000);
            _server = _system.ActorOf(Props.Create(() => new EchoServer(port)));
            _client = _system.ActorOf(Props.Create(() => new Client("127.0.0.1", port)));
        }
        
        [GlobalCleanup]
        public void Cleanup()
        {
            _system.Dispose();
        }

        [Benchmark]
        public async Task ClientServerCommunication()
        {
            await _client.Ask<CommunicationFinished>(new CommunicationRequest(MessageCount, _message));
        }

        public class CommunicationRequest
        {
            public CommunicationRequest(int totalMessageCount, byte[] message)
            {
                TotalMessageCount = totalMessageCount;
                Message = message;
            }

            public int TotalMessageCount { get; }
            public byte[] Message { get; }
        }
        
        public class CommunicationFinished { }
        
        private class EchoServer : ReceiveActor
        {
            public EchoServer(int port)
            {
                Context.System.Tcp().Tell(new Tcp.Bind(Self, new IPEndPoint(IPAddress.Any, port)));
                
                Receive<Tcp.Bound>(_ => { });
                Receive<Tcp.Connected>(connected =>
                {
                    var connection = Context.ActorOf(Props.Create(() => new EchoConnection(Sender)));
                    Sender.Tell(new Tcp.Register(connection));
                });
            }
        }
        
        private class EchoConnection : ReceiveActor
        {
            public EchoConnection(IActorRef connection)
            {
                Receive<Tcp.Received>(received =>
                {
                    connection.Tell(Tcp.Write.Create(received.Data));
                });

            }
        }
        
        private class Client : ReceiveActor
        {
            private int _totalMessageCount;
            private byte[] _message;
            private int _receivedCount = 0;
            private readonly DnsEndPoint _endpoint;
            private IActorRef _requester;

            public Client(string host, int port)
            {
                _endpoint = new DnsEndPoint(host, port);
                Become(Waiting);
            }
            
            private void Waiting()
            {
                Receive<CommunicationRequest>(request =>
                {
                    _receivedCount = 0;
                    _totalMessageCount = request.TotalMessageCount;
                    _message = request.Message;
                    _requester = Sender;
                    
                    Context.System.Tcp().Tell(new Tcp.Connect(_endpoint));
                });
                Receive<Tcp.Connected>(_ =>
                {
                    Sender.Tell(new Tcp.Register(Self));
                    Sender.Tell(Tcp.Write.Create(ByteString.FromBytes(_message)));
                    
                    Become(() => Connected(Sender));
                });
                Receive<Tcp.CommandFailed>(_ => throw new Exception("Connection failed"));
            }

            private void Connected(IActorRef connection)
            {
                Receive<Tcp.Received>(_ =>
                {
                    _receivedCount++;
                    if (_receivedCount >= _totalMessageCount)
                    {
                        _requester.Tell(new CommunicationFinished());
                        Become(Waiting);
                    }
                    else
                    {
                        connection.Tell(Tcp.Write.Create(ByteString.FromBytes(_message)));
                    }
                });
            }
        }
    }
}