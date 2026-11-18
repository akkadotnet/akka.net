//-----------------------------------------------------------------------
// <copyright file="TcpOperationsBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Event;
using Akka.IO;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Tcp = Akka.IO.Tcp;

namespace Akka.Benchmarks
{
    [Config(typeof(MacroBenchmarkConfig))]
    [MemoryDiagnoser]
    public class TcpOperationsBenchmarks
    {
        private ActorSystem _system = null!;
        private byte[] _message = null!;
        
        public const int MessageCount = 1_000_000;

        [Params(10, 100)]
        public int MessageLength { get; set; }

        [Params(1, 3, 5, 7, 10, 20, 30, 40)]
        public int ClientsCount { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            Console.WriteLine("TCP benchmark GC: server={0}, runtime={1}, processors={2}",
                GCSettings.IsServerGC, Environment.Version, Environment.ProcessorCount);
            foreach (var setting in GC.GetConfigurationVariables())
                Console.WriteLine("TCP benchmark GC config: {0}={1}", setting.Key, setting.Value);
            _system = ActorSystem.Create("system", "akka.log-dead-letters = off");
            _message = new byte[MessageLength];
            for (var i = 0; i < _message.Length; i++)
                _message[i] = (byte)i;
        }

        [GlobalCleanup]
        public Task Cleanup() => _system.Terminate();

        private IActorRef _server = null!;
        private IActorRef _clientCoordinator = null!;
        
        [IterationSetup]
        public void IterationSetup()
        {
            // Create new server and client before each iteration measurement
            _server = _system.ActorOf(Props.Create(() => new EchoServer(MessageLength)));
            _clientCoordinator = _system.ActorOf(Props.Create(() => new ClientCoordinator(_server, ClientsCount)));
        }
        
        private async Task StopIterationAsync()
        {
            // Awaited inside the invocation: this cleanup is included in the measured operation.
            var clientStop = _clientCoordinator.GracefulStop(TimeSpan.FromSeconds(3));
            var serverStop = _server.GracefulStop(TimeSpan.FromSeconds(3));
            await Task.WhenAll(clientStop, serverStop);
        }
        
        [Benchmark(OperationsPerInvoke = MessageCount)]
        public async Task ClientServerCommunication()
        {
            // IterationSetup is unmeasured; communication and awaited cleanup are measured.
            try
            {
                var result = await _clientCoordinator.Ask<CommunicationFinished>(
                    new CommunicationRequest(MessageCount, _message), TimeSpan.FromMinutes(5));
                if (result.Messages != MessageCount)
                    throw new InvalidOperationException($"Expected {MessageCount} echoes, received {result.Messages}.");
            }
            finally
            {
                await StopIterationAsync();
            }
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

        public sealed class CommunicationFinished
        {
            public CommunicationFinished(int messages) => Messages = messages;
            public int Messages { get; }
        }

        public sealed class ChildCommunicationFinished
        {
            public ChildCommunicationFinished(int messages) => Messages = messages;
            public int Messages { get; }
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
            private readonly int _messageSize;
            public EchoServer(int messageSize)
            {
                _messageSize = messageSize;
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
                    var connectionRef = Sender;
                    var connection = Context.ActorOf(Props.Create(() => new EchoConnection(connectionRef, _messageSize)));
                    connectionRef.Tell(new Tcp.Register(connection));
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
            private readonly Framer _fr;
            
            public EchoConnection(IActorRef connection, int messageSize)
            {
                _fr = new Framer(messageSize);
                Receive<Tcp.Received>(received =>
                { 
                    foreach(var m in _fr.Deframe(received.Data))
                    {
                        // echo the message back
                        connection.Tell(Tcp.Write.Create(m));
                    }
                });
            }
        }

        private class ClientCoordinator : ReceiveActor, IWithTimers, IWithStash
        {
            private readonly IActorRef _echoServer;
            private readonly HashSet<IActorRef> _waitingChildren = new();
            private IActorRef _requester = null!;
            private readonly int _clientsCount;
            private int _expectedMessages;
            private int _completedMessages;
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
                    _expectedMessages = request.MessagesToSend;
                    _completedMessages = 0;
                    var remainder = request.MessagesToSend % _clientsCount;
                    var messagesPerActor = request.MessagesToSend / _clientsCount;
                    for (var i = 0; i < _clientsCount; ++i)
                    {
                        var childMessages = messagesPerActor + (i < remainder ? 1 : 0);
                        var child = Context.ActorOf(Props.Create(() =>
                            new Client(_endpoint!, childMessages, request.Message)));
                        _waitingChildren.Add(child);
                    }
                });
                
                Receive<ChildCommunicationFinished>(completed =>
                {
                    if (!_waitingChildren.Remove(Sender))
                    {
                        FailCommunication("Duplicate TCP client completion.");
                        return;
                    }
                    Context.Stop(Sender);
                    _completedMessages += completed.Messages;

                    if (_waitingChildren.Count == 0)
                    {
                        if (_completedMessages != _expectedMessages)
                        {
                            FailCommunication("TCP clients completed with a missing echo.");
                            return;
                        }
                        _requester.Tell(new CommunicationFinished(_completedMessages));
                    }
                });

                Receive<Status.Failure>(failure =>
                {
                    // blow up the benchmark
                    _requester.Tell(failure);
                    Context.Stop(Self);
                });
                
                ServerDiedHandler();
            }

            private void FailCommunication(string reason)
            {
                _requester.Tell(new Status.Failure(new InvalidOperationException(reason)));
                Context.Stop(Self);
            }

            protected override void PreStart()
            {
                Timers.StartSingleTimer("BenchmarkTimeout", new Status.Failure(new Exception("Benchmark timed out")),
                    TimeSpan.FromMinutes(5));
                
                Context.WatchWith(_echoServer, ServerDied.Instance);
                _echoServer.Tell(GetBindAddress.Instance);
            }

            public ITimerScheduler Timers { get; set; } = null!;
            public IStash Stash { get; set; } = null!;
        }

        private sealed class Framer
        {
            private readonly int _messageSize;
            private ReadOnlySequence<byte> _partialRead = ReadOnlySequence<byte>.Empty;

            public Framer(int messageSize)
            {
                _messageSize = messageSize;
            }

            public bool HasPartial => !_partialRead.IsEmpty;

            public IEnumerable<ReadOnlySequence<byte>> Deframe(ReadOnlySequence<byte> data)
            {
                // Prepend any partial read from last time
                if (_partialRead.Length > 0)
                {
                    var partialLen = (int)_partialRead.Length;
                    var combined = new byte[partialLen + (int)data.Length];
                    _partialRead.CopyTo(combined.AsSpan(0, partialLen));
                    data.CopyTo(combined.AsSpan(partialLen));
                    data = new ReadOnlySequence<byte>(combined);
                    _partialRead = ReadOnlySequence<byte>.Empty;
                }

                var msgs = new List<ReadOnlySequence<byte>>();
                int offset = 0;
                while (offset + _messageSize <= data.Length)
                {
                    msgs.Add(data.Slice(offset, _messageSize));
                    offset += _messageSize;
                }

                // Buffer any remaining bytes for next time
                if (offset < data.Length)
                {
                    _partialRead = data.Slice(offset, (int)data.Length - offset);
                }

                return msgs;
            }
        }

        private class Client : ReceiveActor, IWithTimers
        {
            private readonly ILoggingAdapter _log = Context.GetLogger();
            private int _receivedCount;
            private int _sentCount;
            private readonly byte[] _expectedMessage;
            private readonly int _messagesToSend;
            private IActorRef _connection = null!;
            private int _connectAttemptsRemaining = 5;
            
            private Framer _framer;

            private class RetryConnect
            {
                public static RetryConnect Instance { get; } = new();

                private RetryConnect()
                {
                }
            }
            

            public Client(EndPoint endpoint, int messagesToSend, byte[] message)
            {
                _framer = new Framer(message.Length);
                _expectedMessage = message;
                _messagesToSend = messagesToSend;
                var write =
                    // create the write only once
                    Tcp.Write.Create(message.AsMemory());
                
                DoConnect(endpoint);
                Receive<Tcp.Connected>(_ =>
                {
                    Sender.Tell(new Tcp.Register(Self));
                    
                    // send messages in bursts of 20
                    for (var i = 0; i < Math.Min(20, messagesToSend); i++)
                    {
                        Sender.Tell(write);
                        _sentCount++;
                    }
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
                Receive<Tcp.Received>(r =>
                {
                    foreach (var m in _framer.Deframe(r.Data))
                    {
                        if (!ValidateEcho(m))
                            return;
                        _receivedCount++;
                        if (_receivedCount > messagesToSend)
                        {
                            FailCommunication("TCP benchmark received an extra echo.");
                            return;
                        }
                        if (_receivedCount == messagesToSend)
                        {
                            _connection.Tell(Tcp.Close.Instance);
                        }
                        else if (_sentCount < messagesToSend)
                        {
                            _connection.Tell(write);
                            _sentCount++;
                        }
                    }
                });
                Receive<Tcp.Closed>(_ =>
                {
                    if (_receivedCount != _messagesToSend || _sentCount != _messagesToSend)
                    {
                        FailCommunication("TCP connection closed before all echoes were delivered.");
                        return;
                    }
                    if (_framer.HasPartial)
                    {
                        FailCommunication("TCP connection closed with an incomplete trailing echo.");
                        return;
                    }
                    Context.Parent.Tell(new ChildCommunicationFinished(_receivedCount));
                });
            }

            private bool ValidateEcho(ReadOnlySequence<byte> message)
            {
                if (message.Length != _expectedMessage.Length)
                {
                    FailCommunication("TCP benchmark echo length changed.");
                    return false;
                }
                var offset = 0;
                foreach (var segment in message)
                {
                    foreach (var value in segment.Span)
                    {
                        if (value != _expectedMessage[offset++])
                        {
                            FailCommunication("TCP benchmark echo payload changed.");
                            return false;
                        }
                    }
                }
                return true;
            }

            private void FailCommunication(string reason)
            {
                Context.Parent.Tell(new Status.Failure(new InvalidOperationException(reason)));
                if (_connection is not null)
                    _connection.Tell(Tcp.Abort.Instance);
                Context.Stop(Self);
            }

            private static void DoConnect(EndPoint endpoint)
            {
                Context.System.Tcp().Tell(new Tcp.Connect(endpoint, timeout: TimeSpan.FromSeconds(5)));
            }

            public ITimerScheduler Timers { get; set; } = null!;
        }
    }
}
