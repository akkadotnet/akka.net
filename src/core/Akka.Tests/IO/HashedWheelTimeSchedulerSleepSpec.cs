// //-----------------------------------------------------------------------
// // <copyright file="HashedWheelTimeSchedulerSleepSpec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.IO
{
    public class HashedWheelTimeSchedulerSleepSpec : IDisposable
    {
        private const int ClientsCount = 1;
        
        private readonly ITestOutputHelper _outputHelper;
        private readonly ActorSystem _system;
        private readonly IActorRef _server;
        private readonly IActorRef _clientCoordinator;

        public HashedWheelTimeSchedulerSleepSpec(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
            _system = ActorSystem.Create("system");

            var port = new Random().Next(18000, 19000);
            _server = _system.ActorOf(Props.Create(() => new EchoServer(port)));
            _clientCoordinator = _system.ActorOf(Props.Create(() => new ClientCoordinator("127.0.0.1", port, ClientsCount)));
        }

        public void Dispose()
        {
            _system.Dispose();
        }

        [Theory]
        [InlineData(10000, 100, 5)]
        public async Task ClientServerCommunication(int messageCount, int messageLength, int iterations)
        {
            var message = new byte[messageLength];
            for (var i = 0; i < iterations; ++i)
            {
                await ExecuteClientServerCommunication(messageCount, message);
                _outputHelper.WriteLine($"Finished #{i}");
            }

            AssertTimingsAreSmallEnough();
        }

        private void AssertTimingsAreSmallEnough()
        {
            var msRequired = HashedWheelTimerScheduler.TotalTicksRequiredToWaitStrict.Current * 1.0M / TimeSpan.TicksPerSecond;
            var msActual = HashedWheelTimerScheduler.TotalTicksActual.Current * 1.0M / TimeSpan.TicksPerSecond;
            
            _outputHelper.WriteLine($"Required wait time is {msRequired}ms, and actual is {msActual}ms");
            
            msActual.ShouldBeLessThan(msRequired * 1.1M, "We absolutelly do not want scheduler to have more then 10% sleep overhead");
            msActual.ShouldBeLessThan(msRequired * 1.05M, "We do not want scheduler to have more then 5% sleep overhead");
            msActual.ShouldBeLessThan(msRequired * 1.01M, "Would be really nice for scheduler to have less then 1% sleep overhead");
        }

        private async Task ExecuteClientServerCommunication(int countOfMessages, byte[] message)
        {
            await _clientCoordinator.Ask<CommunicationFinished>(new CommunicationRequest(countOfMessages, message));
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
    }
}