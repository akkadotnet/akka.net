//-----------------------------------------------------------------------
// <copyright file="TcpOperationsBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
        private IActorRef _clientCoordinator;

        [Params(100, 1000, 10000)]
        public int MessageCount { get; set; }
        [Params(10, 100)]
        public int MessageLength { get; set; }
        [Params(1, 3, 5, 7, 10, 20, 30, 40)]
        public int ClientsCount { get; set; }
        
        [GlobalSetup]
        public void Setup()
        {
            _system = ActorSystem.Create("system");
            _message = new byte[MessageLength];

            var port = GetFreeTcpPort();
            _server = _system.ActorOf(Props.Create(() => new EchoServer(port)));
            _clientCoordinator = _system.ActorOf(Props.Create(() => new ClientCoordinator("127.0.0.1", port, ClientsCount)));
        }
        
        [GlobalCleanup]
        public void Cleanup()
        {
            _system.Dispose();
        }

        [Benchmark]
        public async Task ClientServerCommunication()
        {
            await _clientCoordinator.Ask<CommunicationFinished>(new CommunicationRequest(MessageCount, _message));
        }

        public class CommunicationRequest
        {
            public CommunicationRequest(int messagesToSend, byte[] message)
            {
                MessagesToSend = messagesToSend;
                Message = message;
            }

            public int MessagesToSend { get; }
            public byte[] Message { get; }
        }
        
        public class CommunicationFinished { }
        public class ChildCommunicationFinished { }
        
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

        private class ClientCoordinator : ReceiveActor
        {
            private readonly HashSet<IActorRef> _waitingChildren = new HashSet<IActorRef>();
            private IActorRef _requester;
            
            public ClientCoordinator(string host, int port, int clientsCount)
            {
                var endpoint = new DnsEndPoint(host, port);
                Receive<CommunicationRequest>(request =>
                {
                    _requester = Sender;
                    var messagesPerActor = request.MessagesToSend / clientsCount;
                    for (var i = 0; i < clientsCount; ++i)
                    {
                        var child = Context.ActorOf(Props.Create(() => new Client(endpoint, messagesPerActor, request.Message)));
                        _waitingChildren.Add(child);
                    }
                });
                Receive<ChildCommunicationFinished>(_ =>
                {
                    Context.Stop(Sender);
                    
                    _waitingChildren.Remove(Sender);
                    
                    if (_waitingChildren.Count == 0)
                        _requester.Tell(new CommunicationFinished());
                });
            }
        }
        
        private class Client : ReceiveActor
        {
            private int _receivedCount = 0;
            private IActorRef _connection;

            public Client(DnsEndPoint endpoint, int messagesToSend, byte[] message)
            {
                Context.System.Tcp().Tell(new Tcp.Connect(endpoint));
                Receive<Tcp.Connected>(_ =>
                {
                    Sender.Tell(new Tcp.Register(Self));
                    Sender.Tell(Tcp.Write.Create(ByteString.FromBytes(message)));
                    _connection = Sender;
                });
                Receive<Tcp.CommandFailed>(_ => throw new Exception("Connection failed"));
                Receive<Tcp.Received>(_ =>
                {
                    _receivedCount++;
                    if (_receivedCount >= messagesToSend)
                    {
                        _connection.Tell(Tcp.Close.Instance);
                    }
                    else
                    {
                        _connection.Tell(Tcp.Write.Create(ByteString.FromBytes(message)));
                    }
                });
                Receive<Tcp.Closed>(_ =>
                {
                    Context.Parent.Tell(new ChildCommunicationFinished());
                });
            }
        }
        
        private static int GetFreeTcpPort()
        {
            var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }
    }
}
