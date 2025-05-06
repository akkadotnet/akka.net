//-----------------------------------------------------------------------
// <copyright file="TcpOperationsBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Event;
using Akka.IO;
using BenchmarkDotNet.Attributes;
using Tcp = Akka.IO.Tcp;

namespace Akka.Benchmarks
{
    [Config(typeof(MacroBenchmarkConfig))]
    public class TcpOperationsBenchmarks
    {
        private ActorSystem _system;
        private byte[] _message;
        private IActorRef _server;
        private IActorRef _clientCoordinator;
        
        public int MessageCount { get; } = 10_000;

        [Params(10, 100)]
        public int MessageLength { get; set; }

        [Params(1, 3, 5, 7, 10, 20, 30, 40)]
        public int ClientsCount { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _system = ActorSystem.Create("system");
            _message = new byte[MessageLength];
            
            _server = _system.ActorOf(Props.Create(() => new EchoServer()));
            _clientCoordinator =
                _system.ActorOf(Props.Create(() => new ClientCoordinator(_server, ClientsCount)));
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _system.Dispose();
        }

        [Benchmark(OperationsPerInvoke = 10_000)]
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

        public class CommunicationFinished
        {
        }

        public class ChildCommunicationFinished
        {
        }
        
        public sealed class GetBindAddress
        {
            public static GetBindAddress Instance { get; } = new();
            private GetBindAddress()
            {
            }
        }

        private class EchoServer : ReceiveActor, IWithStash
        {
            private EndPoint? _endpoint;
            
            public EchoServer()
            {
                Context.System.Tcp().Tell(new Tcp.Bind(Self, new IPEndPoint(IPAddress.Loopback, 0)));

                Receive<Tcp.Bound>(bound =>
                {
                    _endpoint = bound.LocalAddress;
                    Become(Bound);
                    Stash!.UnstashAll();
                });
                
                Receive<Tcp.CommandFailed>(f =>
                {
                    // log a detailed error
                    if (f.Cause.HasValue)
                    {
                        Context.System.Log.Error(f.Cause.Value, "Command [{0}] failed with error [{1}]", f.Cmd,
                            f.CauseString);
                    }
                    else
                    {
                        Context.System.Log.Error("Command [{0}] failed with error [{1}]", f.Cmd, f.CauseString);
                    }

                    // blow up the benchmark
                    Context.Stop(Self);
                });
                
                ReceiveAny(_ => Stash.Stash());
            }

            private void Bound()
            {
                Receive<Tcp.Connected>(_ =>
                {
                    var connection = Context.ActorOf(Props.Create(() => new EchoConnection(Sender)));
                    Sender.Tell(new Tcp.Register(connection));
                });
                
                Receive<Tcp.CommandFailed>(f =>
                {
                    // log a detailed error
                    if (f.Cause.HasValue)
                    {
                        Context.System.Log.Error(f.Cause.Value, "Command [{0}] failed with error [{1}]", f.Cmd,
                            f.CauseString);
                    }
                    else
                    {
                        Context.System.Log.Error("Command [{0}] failed with error [{1}]", f.Cmd, f.CauseString);
                    }

                    // blow up the benchmark
                    Context.Stop(Self);
                });
                
                Receive<GetBindAddress>(_ =>
                {
                    Sender.Tell(_endpoint);
                });
            }

            public IStash Stash { get; set; } = null!;
        }

        private class EchoConnection : ReceiveActor
        {
            public EchoConnection(IActorRef connection)
            {
                Receive<Tcp.Received>(received => { connection.Tell(Tcp.Write.Create(received.Data)); });
            }
        }

        private class ClientCoordinator : ReceiveActor, IWithTimers, IWithStash
        {
            private readonly IActorRef _echoServer;
            private readonly HashSet<IActorRef> _waitingChildren = new();
            private IActorRef _requester;
            private readonly int _clientsCount;
            private EndPoint? _endpoint;
            
            private class ServerDied
            {
                public static ServerDied Instance { get; } = new();
                private ServerDied()
                {
                }
            }

            public ClientCoordinator(IActorRef echoServer, int clientsCount)
            {
                _echoServer = echoServer;
                _clientsCount = clientsCount;
                
                Receive<EndPoint>(endpoint =>
                {
                    _endpoint = endpoint;
                    Become(Bound);
                    Stash!.UnstashAll();
                });
                
                ServerDiedHandler();
                
                ReceiveAny(_ =>
                {
                    // stash messages until we have the endpoint
                    Stash.Stash();
                });
                
                
            }

            private void ServerDiedHandler()
            {
                Receive<ServerDied>(_ =>
                {
                    // blow up the benchmark
                    _requester.Tell(new Status.Failure(new Exception("Server died")));
                    Context.Stop(Self);
                });
            }

            private void Bound()
            {
                Receive<CommunicationRequest>(request =>
                {
                    _requester = Sender;
                    var messagesPerActor = request.MessagesToSend / _clientsCount;
                    for (var i = 0; i < _clientsCount; ++i)
                    {
                        var child = Context.ActorOf(Props.Create(() =>
                            new Client(_endpoint!, messagesPerActor, request.Message)));
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

                Receive<Status.Failure>(failure =>
                {
                    // blow up the benchmark
                    _requester.Tell(failure);
                    Context.Stop(Self);
                });
                
                ServerDiedHandler();
            }

            protected override void PreStart()
            {
                Timers.StartSingleTimer("BenchmarkTimeout", new Status.Failure(new Exception("Benchmark timed out")),
                    TimeSpan.FromSeconds(60));
                
                Context.WatchWith(_echoServer, ServerDied.Instance);
                _echoServer.Tell(GetBindAddress.Instance);
            }

            public ITimerScheduler Timers { get; set; }
            public IStash Stash { get; set; }
        }

        private class Client : ReceiveActor, IWithTimers
        {
            private readonly ILoggingAdapter _log = Context.GetLogger();
            private int _receivedCount = 0;
            private IActorRef _connection;
            private int _connectAttemptsRemaining = 5;

            private class RetryConnect
            {
                public static RetryConnect Instance { get; } = new();

                private RetryConnect()
                {
                }
            }

            public Client(EndPoint endpoint, int messagesToSend, byte[] message)
            {
                DoConnect(endpoint);
                Receive<Tcp.Connected>(_ =>
                {
                    Sender.Tell(new Tcp.Register(Self));
                    Sender.Tell(Tcp.Write.Create(ByteString.FromBytes(message)));
                    _connection = Sender;
                });
                Receive<RetryConnect>(_ => { DoConnect(endpoint); });
                Receive<Tcp.CommandFailed>(f =>
                {
                    if (f.Cause.HasValue)
                    {
                        _log.Error(f.Cause.Value, "Command [{0}] failed with error [{1}]", f.Cmd, f.CauseString);
                    }
                    else
                    {
                        _log.Error("Command [{0}] failed with error [{1}]", f.Cmd, f.CauseString);
                    }

                    if (_connectAttemptsRemaining > 0)
                    {
                        _connectAttemptsRemaining--;
                        _log.Debug("Retrying connection to {0}", endpoint);
                        DoConnect(endpoint);
                    }
                    else
                    {
                        // blow up the test
                        _log.Error("Failed to connect to {0} after 5 attempts", endpoint);
                        Context.Parent.Tell(new Status.Failure(new Exception("Failed to connect after 5 attempts")));
                    }
                    Timers.StartSingleTimer("RetryConnect", RetryConnect.Instance, TimeSpan.FromMilliseconds(20));
                });
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
                Receive<Tcp.Closed>(_ => { Context.Parent.Tell(new ChildCommunicationFinished()); });
            }

            private static void DoConnect(EndPoint endpoint)
            {
                Context.System.Tcp().Tell(new Tcp.Connect(endpoint, timeout: TimeSpan.FromSeconds(5)));
            }

            public ITimerScheduler Timers { get; set; }
        }
    }
}