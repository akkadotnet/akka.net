//-----------------------------------------------------------------------
// <copyright file="TcpIntegrationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.TestKit;
using Akka.Util;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;
#if CORECLR
using System.Runtime.InteropServices;
#endif

namespace Akka.Tests.IO
{
    public class TcpIntegrationSpec : AkkaSpec
    {
        public const int InternalConnectionActorMaxQueueSize = 10000;
        
        class Aye : Tcp.Event { public static readonly Aye Instance = new Aye(); }
        class Yes : Tcp.Event { public static readonly Yes Instance = new Yes(); }
        class Ack : Tcp.Event { public static readonly Ack Instance = new Ack(); }

        class AckWithValue : Tcp.Event
        {
            public object Value { get; }
            public static AckWithValue Create(object value) => new AckWithValue(value);
            private AckWithValue(object value) { Value = value; }
        }

        
        public TcpIntegrationSpec(ITestOutputHelper output)
            : base($@"akka.loglevel = DEBUG
                     akka.io.tcp.trace-logging = true
                     akka.io.tcp.write-commands-queue-max-size = {InternalConnectionActorMaxQueueSize}", output: output)
        { }

        private void VerifyActorTermination(IActorRef actor)
        {
            Watch(actor);
            ExpectTerminated(actor);
        }

        [Fact]
        public void The_TCP_transport_implementation_should_properly_bind_a_test_server()
        {
            new TestSetup(this).Run(x => { });
        }

        [Fact(Skip="FIXME .net core / linux")]
        public void The_TCP_transport_implementation_should_allow_connecting_to_and_disconnecting_from_the_test_server()
        {
            new TestSetup(this).Run(x =>
            {
                var actors = x.EstablishNewClientConnection();
                actors.ClientHandler.Send(actors.ClientConnection, Tcp.Close.Instance);
                actors.ClientHandler.ExpectMsg<Tcp.Closed>();
                
                actors.ServerHandler.ExpectMsg<Tcp.PeerClosed>();
                VerifyActorTermination(actors.ClientConnection);
                VerifyActorTermination(actors.ServerConnection);
            });
        }

        [Fact(Skip="FIXME .net core / linux")]
        public void The_TCP_transport_implementation_should_properly_handle_connection_abort_from_client_side()
        {
            new TestSetup(this).Run(x =>
            {
                var actors = x.EstablishNewClientConnection();
                actors.ClientHandler.Send(actors.ClientConnection, Tcp.Abort.Instance);
                actors.ClientHandler.ExpectMsg<Tcp.Aborted>();
                actors.ServerHandler.ExpectMsg<Tcp.ErrorClosed>();
                VerifyActorTermination(actors.ClientConnection);
                VerifyActorTermination(actors.ServerConnection);
            });
        }

        [Fact(Skip="FIXME .net core / linux")]
        public void The_TCP_transport_implementation_should_properly_handle_connection_abort_from_client_side_after_chit_chat()
        {
            new TestSetup(this).Run(x =>
            {
                var actors = x.EstablishNewClientConnection();
                ChitChat(actors);

                actors.ClientHandler.Send(actors.ClientConnection, Tcp.Abort.Instance);
                actors.ClientHandler.ExpectMsg<Tcp.Aborted>();
                actors.ServerHandler.ExpectMsg<Tcp.ErrorClosed>();
                VerifyActorTermination(actors.ClientConnection);
                VerifyActorTermination(actors.ServerConnection);
            });   
        }

        [Fact]
        public void The_TCP_transport_implementation_should_properly_handle_connection_abort_via_PoisonPill_from_client_side()
        {
            new TestSetup(this).Run(x =>
            {
                var actors = x.EstablishNewClientConnection();
                actors.ClientHandler.Send(actors.ClientConnection, PoisonPill.Instance);
                VerifyActorTermination(actors.ClientConnection);

                actors.ServerHandler.ExpectMsg<Tcp.ErrorClosed>();
                VerifyActorTermination(actors.ServerConnection);
            });
        }

        [Fact]
        public void The_TCP_transport_implementation_should_properly_handle_connection_abort_via_PoisonPill_from_client_side_after_chit_chat()
        {
            new TestSetup(this).Run(x =>
            {
                var actors = x.EstablishNewClientConnection();
                ChitChat(actors);

                actors.ClientHandler.Send(actors.ClientConnection, PoisonPill.Instance);
                VerifyActorTermination(actors.ClientConnection);

                actors.ServerHandler.ExpectMsg<Tcp.ErrorClosed>();
                VerifyActorTermination(actors.ServerConnection);
            });
        }

        [Fact]
        public void The_TCP_transport_implementation_should_properly_handle_connection_abort_via_PoisonPill_from_server_side()
        {
            new TestSetup(this).Run(x =>
            {
                var actors = x.EstablishNewClientConnection();
                actors.ServerHandler.Send(actors.ServerConnection, PoisonPill.Instance);
                VerifyActorTermination(actors.ServerConnection);

                actors.ClientHandler.ExpectMsg<Tcp.ErrorClosed>();
                VerifyActorTermination(actors.ClientConnection);
            });
        }

        [Fact]
        public void The_TCP_transport_implementation_should_properly_handle_connection_abort_via_PoisonPill_from_server_side_after_chit_chat()
        {
            new TestSetup(this).Run(x =>
            {
                var actors = x.EstablishNewClientConnection();
                ChitChat(actors);

                actors.ServerHandler.Send(actors.ServerConnection, PoisonPill.Instance);
                VerifyActorTermination(actors.ServerConnection);

                actors.ClientHandler.ExpectMsg<Tcp.ErrorClosed>();
                VerifyActorTermination(actors.ClientConnection);
            });
        }

        [InlineData(AddressFamily.InterNetworkV6)]
        [InlineData(AddressFamily.InterNetwork)]
        [Theory]
        public void The_TCP_transport_implementation_should_properly_support_connecting_to_DNS_endpoints(AddressFamily family)
        {
            // Aaronontheweb, 9/2/2017 - POSIX-based OSES are still having trouble with IPV6 DNS resolution
#if CORECLR
            if(!System.Runtime.InteropServices.RuntimeInformation
                .IsOSPlatform(OSPlatform.Windows) && family == AddressFamily.InterNetworkV6)
            return;
#else
            if (RuntimeDetector.IsMono && family == AddressFamily.InterNetworkV6) // same as above
                return;
#endif
            var serverHandler = CreateTestProbe();
            var bindCommander = CreateTestProbe();
            bindCommander.Send(Sys.Tcp(), new Tcp.Bind(serverHandler.Ref, new IPEndPoint(family == AddressFamily.InterNetwork ? IPAddress.Loopback 
                : IPAddress.IPv6Loopback, 0)));
            var boundMsg = bindCommander.ExpectMsg<Tcp.Bound>();

            // setup client to connect 
            var targetAddress = new DnsEndPoint("localhost", boundMsg.LocalAddress.AsInstanceOf<IPEndPoint>().Port);
            var clientHandler = CreateTestProbe();
            Sys.Tcp().Tell(new Tcp.Connect(targetAddress), clientHandler);
            clientHandler.ExpectMsg<Tcp.Connected>(TimeSpan.FromMinutes(10));
            var clientEp = clientHandler.Sender;
            clientEp.Tell(new Tcp.Register(clientHandler));
            serverHandler.ExpectMsg<Tcp.Connected>();
            serverHandler.Sender.Tell(new Tcp.Register(serverHandler));

            var str = Enumerable.Repeat("f", 567).Join("");
            var testData = ByteString.FromString(str);
            clientEp.Tell(Tcp.Write.Create(testData, Ack.Instance), clientHandler);
            clientHandler.ExpectMsg<Ack>();
            var received = serverHandler.ReceiveWhile<Tcp.Received>(o =>
            {
                return o as Tcp.Received;
            }, RemainingOrDefault, TimeSpan.FromSeconds(0.5));

            received.Sum(s => s.Data.Count).Should().Be(testData.Count);
        }

        [Fact]
        public void BugFix_3021_Tcp_Should_not_drop_large_messages()
        {
            new TestSetup(this).Run(x =>
            {
                var actors = x.EstablishNewClientConnection();

                // create a large-ish byte string
                var str = Enumerable.Repeat("f", 567).Join("");
                var testData = ByteString.FromString(str);

                // queue 3 writes
                actors.ClientHandler.Send(actors.ClientConnection, Tcp.Write.Create(testData));
                actors.ClientHandler.Send(actors.ClientConnection, Tcp.Write.Create(testData));
                actors.ClientHandler.Send(actors.ClientConnection, Tcp.Write.Create(testData));

                var serverMsgs = actors.ServerHandler.ReceiveWhile<Tcp.Received>(o =>
                {
                    return o as Tcp.Received;
                }, RemainingOrDefault, TimeSpan.FromSeconds(2));

                serverMsgs.Sum(s => s.Data.Count).Should().Be(testData.Count*3);
            });
        }

        [Fact]
        public void When_sending_Close_to_TcpManager_Should_log_detailed_error_message()
        {
            new TestSetup(this).Run(x =>
            {
                // Setup multiple clients
                var actors = x.EstablishNewClientConnection();

                // Error message should contain invalid message type
                EventFilter.Error(contains: nameof(Tcp.Close)).ExpectOne(() =>
                {
                    // Sending `Tcp.Close` to TcpManager instead of outgoing connection
                    Sys.Tcp().Tell(Tcp.Close.Instance, actors.ClientHandler);
                });
                // Should also contain ref to documentation
                EventFilter.Error(contains: "https://getakka.net/articles/networking/io.html").ExpectOne(() =>
                {
                    // Sending `Tcp.Close` to TcpManager instead of outgoing connection
                    Sys.Tcp().Tell(Tcp.Close.Instance, actors.ClientHandler);
                });
            });
        }

        [Fact]
        public void Write_before_Register_should_not_be_silently_dropped()
        {
            new TestSetup(this).Run(x =>
            {
                var actors = x.EstablishNewClientConnection(registerClientHandler: false);

                var msg = ByteString.FromString("msg"); // 3 bytes

                EventFilter.Warning(new Regex("Received Write command before Register[^3]+3 bytes")).ExpectOne(() =>
                {
                    actors.ClientHandler.Send(actors.ClientConnection, Tcp.Write.Create(msg));
                    actors.ClientConnection.Tell(new Tcp.Register(actors.ClientHandler));
                });
                
                var serverMsgs = actors.ServerHandler.ReceiveWhile(o =>
                {
                    return o as Tcp.Received;
                }, RemainingOrDefault, TimeSpan.FromSeconds(2));

                serverMsgs.Should().HaveCount(1).And.Subject.Should().Contain(m => m.Data.Equals(msg));
            });
        }
        
        [Fact]
        public void Write_before_Register_should_Be_dropped_if_buffer_is_full()
        {
            new TestSetup(this).Run(x =>
            {
                var actors = x.EstablishNewClientConnection(registerClientHandler: false);

                var overflowData = ByteString.FromBytes(new byte[InternalConnectionActorMaxQueueSize + 1]);

                // We do not want message about receiving Write to be logged, if the write was actually discarded
                EventFilter.Warning(new Regex("Received Write command before Register[^3]+3 bytes")).Expect(0, () =>
                {
                    actors.ClientHandler.Send(actors.ClientConnection, Tcp.Write.Create(overflowData));
                });
                
                actors.ClientHandler.ExpectMsg<Tcp.CommandFailed>(TimeSpan.FromSeconds(10));
                
                // After failed receive, next "good" writes should be handled with no issues
                actors.ClientHandler.Send(actors.ClientConnection, Tcp.Write.Create(ByteString.FromBytes(new byte[1])));
                actors.ClientHandler.Send(actors.ClientConnection, new Tcp.Register(actors.ClientHandler));
                var serverMsgs = actors.ServerHandler.ReceiveWhile(o => o as Tcp.Received, RemainingOrDefault, TimeSpan.FromSeconds(2));
                serverMsgs.Should().HaveCount(1).And.Subject.Should().Contain(m => m.Data.Count == 1);
            });
        }

        [Fact]
        public void When_multiple_concurrent_writing_clients_Should_not_lose_messages()
        {
            const int clientsCount = 50;
            
            new TestSetup(this).Run(x =>
            {
                // Setup multiple clients
                var actors = x.EstablishNewClientConnection();

                // Each client sends his index to server
                var clients = Enumerable.Range(0, clientsCount).Select(i => (Index: i, Probe: CreateTestProbe($"test-client-{i}"))).ToArray();
                var counter = new AtomicCounter(0);
                Parallel.ForEach(clients, client =>
                {
                    var msg = ByteString.FromString(client.Index.ToString());
                    counter.AddAndGet(msg.Count);
                    client.Probe.Send(actors.ClientConnection, Tcp.Write.Create(msg));
                });
                
                // All messages data should be received
                var received = actors.ServerHandler.ReceiveWhile(o => o as Tcp.Received, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1.5));
                received.Sum(r => r.Data.Count).ShouldBe(counter.Current);
            });
        }
        
        [Fact]
        public void When_multiple_concurrent_writing_clients_All_acks_should_be_received()
        {
            const int clientsCount = 50;
            
            new TestSetup(this).Run(x =>
            {
                // Setup multiple clients
                var actors = x.EstablishNewClientConnection();

                // Each client sends his index to server
                var indexRange = Enumerable.Range(0, clientsCount).ToList();
                var clients = indexRange.Select(i => (Index: i, Probe: CreateTestProbe($"test-client-{i}"))).ToArray();
                Parallel.ForEach(clients, client =>
                {
                    var msg = ByteString.FromBytes(new byte[1]);
                    client.Probe.Send(actors.ClientConnection, Tcp.Write.Create(msg, AckWithValue.Create(client.Index)));
                });
                
                // All acks should be received
                clients.ForEach(client =>
                {
                    client.Probe.ExpectMsg<AckWithValue>(ack => ack.Value.ShouldBe(client.Index), TimeSpan.FromSeconds(10));
                });
            });
        }
        
        [Fact]
        public void When_multiple_writing_clients_Should_receive_messages_in_order()
        {
            const int clientsCount = 50;
            
            new TestSetup(this).Run(x =>
            {
                // Setup multiple clients
                var actors = x.EstablishNewClientConnection();

                // Each client sends his index to server
                var clients = Enumerable.Range(0, clientsCount).Select(i => (Index: i, Probe: CreateTestProbe($"test-client-{i}"))).ToArray();
                var contentBuilder = new StringBuilder();
                clients.ForEach(client =>
                {
                    var msg = client.Index.ToString();
                    contentBuilder.Append(msg);
                    client.Probe.Send(actors.ClientConnection, Tcp.Write.Create(ByteString.FromString(msg)));
                });
                
                // All messages data should be received, and be in the same order as they were sent
                var received = actors.ServerHandler.ReceiveWhile(o => o as Tcp.Received, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1.5));
                var content = string.Join("", received.Select(r => r.Data.ToString()));
                content.ShouldBe(contentBuilder.ToString());
            });
        }

        [Fact]
        public async Task Should_fail_writing_when_buffer_is_filled()
        {
            await new TestSetup(this).RunAsync(async x =>
            {
                var actors = x.EstablishNewClientConnection();

                // create a buffer-overflow message
                var overflowData = ByteString.FromBytes(new byte[InternalConnectionActorMaxQueueSize + 1]);
                var goodData = ByteString.FromBytes(new byte[InternalConnectionActorMaxQueueSize]);

                // If test runner is too loaded, let it try ~3 times with 5 pause interval
                await AwaitAssertAsync(() =>
                {
                    // try sending overflow
                    actors.ClientHandler.Send(actors.ClientConnection, Tcp.Write.Create(overflowData)); // this is sent immidiately
                    actors.ClientHandler.Send(actors.ClientConnection, Tcp.Write.Create(overflowData)); // this will try to buffer
                    actors.ClientHandler.ExpectMsg<Tcp.CommandFailed>(TimeSpan.FromSeconds(20));

                    // First overflow data will be received anyway
                    actors.ServerHandler.ReceiveWhile(TimeSpan.FromSeconds(1), m => m as Tcp.Received)
                        .Sum(m => m.Data.Count)
                        .Should().Be(InternalConnectionActorMaxQueueSize + 1);
                
                    // Check that almost-overflow size does not cause any problems
                    actors.ClientHandler.Send(actors.ClientConnection, Tcp.ResumeWriting.Instance); // Recover after send failure
                    actors.ClientHandler.Send(actors.ClientConnection, Tcp.Write.Create(goodData));
                    actors.ServerHandler.ReceiveWhile(TimeSpan.FromSeconds(1), m => m as Tcp.Received)
                        .Sum(m => m.Data.Count)
                        .Should().Be(InternalConnectionActorMaxQueueSize);
                }, TimeSpan.FromSeconds(30 * 3), TimeSpan.FromSeconds(5)); // 3 attempts by ~25 seconds + 5 sec pause
            });
        }

        
        [Fact]
        public void The_TCP_transport_implementation_should_properly_complete_one_client_server_request_response_cycle()
        {
            new TestSetup(this).Run(x =>
            {
                var actors = x.EstablishNewClientConnection();

                actors.ClientHandler.Send(actors.ClientConnection, Tcp.Write.Create(ByteString.FromString("Captain on the bridge!"), Aye.Instance));
                actors.ClientHandler.ExpectMsg(Aye.Instance);
                actors.ServerHandler.ExpectMsg<Tcp.Received>().Data.ToString(Encoding.ASCII).ShouldBe("Captain on the bridge!");

                actors.ServerHandler.Send(actors.ServerConnection, Tcp.Write.Create(ByteString.FromString("For the king!"), Yes.Instance));
                actors.ServerHandler.ExpectMsg(Yes.Instance);
                actors.ClientHandler.ExpectMsg<Tcp.Received>().Data.ToString(Encoding.ASCII).ShouldBe("For the king!");

                actors.ServerHandler.Send(actors.ServerConnection, Tcp.Close.Instance);
                actors.ServerHandler.ExpectMsg<Tcp.Closed>();
                actors.ClientHandler.ExpectMsg<Tcp.PeerClosed>();

                VerifyActorTermination(actors.ClientConnection);
                VerifyActorTermination(actors.ServerConnection);
            });
        }

        
        [Fact]
        public void The_TCP_transport_implementation_should_support_waiting_for_writes_with_backpressure()
        {
            new TestSetup(this).Run(x =>
            {
                x.BindOptions = new[] {new Inet.SO.SendBufferSize(1024)};
                x.ConnectOptions = new[] {new Inet.SO.SendBufferSize(1024)};

                var actors = x.EstablishNewClientConnection();

                actors.ServerHandler.Send(actors.ServerConnection, Tcp.Write.Create(ByteString.FromBytes(new byte[100000]), Ack.Instance));
                actors.ServerHandler.ExpectMsg(Ack.Instance);

                x.ExpectReceivedData(actors.ClientHandler, 100000);
            });
        }

        [Fact]
        public void The_TCP_transport_implementation_dont_report_Connected_when_endpoint_isnt_responding()
        {
            var connectCommander = CreateTestProbe();
            // a "random" endpoint hopefully unavailable since it's in the test-net IP range
            var endpoint = new IPEndPoint(IPAddress.Parse("192.0.2.1"), 23825);
            connectCommander.Send(Sys.Tcp(), new Tcp.Connect(endpoint));
            // expecting CommandFailed or no reply (within timeout)
            var replies = connectCommander.ReceiveWhile(TimeSpan.FromSeconds(1), x => x as Tcp.Connected);
            replies.Count.ShouldBe(0);
        }

        [Fact]
        public void The_TCP_transport_implementation_handle_tcp_connection_actor_death_properly()
        {
            new TestSetup(this, shouldBindServer:false).Run(x =>
            {
                var serverSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                serverSocket.Bind(x.Endpoint);
                serverSocket.Listen(100);

                var connectCommander = CreateTestProbe();
                connectCommander.Send(Sys.Tcp(), new Tcp.Connect(x.Endpoint));

                var accept = serverSocket.Accept();
                var connected = connectCommander.ExpectMsg<Tcp.Connected>();
                connected.RemoteAddress.AsInstanceOf<IPEndPoint>().Port.ShouldBe(x.Endpoint.Port);
                var connectionActor = connectCommander.LastSender;
                connectCommander.Send(connectionActor, PoisonPill.Instance);

                AwaitConditionNoThrow(() =>
                {
                    try
                    {
                        var buffer = new byte[1024];
                        return accept.Receive(buffer) == 0;
                    }
                    catch (SocketException ex)
                    {
                        return true;
                    }
                }, TimeSpan.FromSeconds(3));

                VerifyActorTermination(connectionActor);
            });
        }

        private void ChitChat(TestSetup.ConnectionDetail actors, int rounds = 100)
        {
            var testData = ByteString.FromBytes(new[] {(byte) 0});
            for (int i = 0; i < rounds; i++)
            {
                actors.ClientHandler.Send(actors.ClientConnection, Tcp.Write.Create(testData));
                actors.ServerHandler.ExpectMsg<Tcp.Received>(x => x.Data.Count == 1 && x.Data[0] == 0, hint: $"server didn't received at {i} round");
                actors.ServerHandler.Send(actors.ServerConnection, Tcp.Write.Create(testData));
                actors.ClientHandler.ExpectMsg<Tcp.Received>(x => x.Data.Count == 1 && x.Data[0] == 0, hint: $"client didn't received at {i} round");
            }
        }

        class TestSetup
        {
            private readonly AkkaSpec _spec;
            private readonly bool _shouldBindServer;
            private readonly TestProbe _bindHandler;
            private readonly IPEndPoint _endpoint;

            public TestSetup(AkkaSpec spec, bool shouldBindServer = true)
            {
                BindOptions =  Enumerable.Empty<Inet.SocketOption>();
                ConnectOptions = Enumerable.Empty<Inet.SocketOption>(); 
                _spec = spec;
                _shouldBindServer = shouldBindServer;
                _bindHandler = _spec.CreateTestProbe("bind-handler-probe");
                _endpoint = TestUtils.TemporaryServerAddress();
            }

            public void BindServer()
            {
                var bindCommander = _spec.CreateTestProbe();
                bindCommander.Send(_spec.Sys.Tcp(), new Tcp.Bind(_bindHandler.Ref, _endpoint, options: BindOptions));
                bindCommander.ExpectMsg<Tcp.Bound>(); //TODO: check endpoint
            }

            public ConnectionDetail EstablishNewClientConnection(bool registerClientHandler = true)
            {
                var connectCommander = _spec.CreateTestProbe("connect-commander-probe");
                connectCommander.Send(_spec.Sys.Tcp(), new Tcp.Connect(_endpoint, options: ConnectOptions));
                connectCommander.ExpectMsg<Tcp.Connected>();
                
                var clientHandler = _spec.CreateTestProbe($"client-handler-probe");
                if (registerClientHandler)
                    connectCommander.Sender.Tell(new Tcp.Register(clientHandler.Ref));

                _bindHandler.ExpectMsg<Tcp.Connected>();
                var serverHandler = _spec.CreateTestProbe("server-handler-probe");
                _bindHandler.Sender.Tell(new Tcp.Register(serverHandler.Ref));

                return new ConnectionDetail
                {
                    ClientHandler = clientHandler,
                    ClientConnection = connectCommander.Sender,
                    ServerHandler = serverHandler,
                    ServerConnection = _bindHandler.Sender
                };
            }

            public class ConnectionDetail
            {
                public TestProbe ClientHandler { get; set; }
                public IActorRef ClientConnection { get; set; }
                public TestProbe ServerHandler { get; set; }
                public IActorRef ServerConnection { get; set; }
            }

            public void ExpectReceivedData(TestProbe handler, int remaining)
            {
                if (remaining > 0)
                {
                    var recv = handler.ExpectMsg<Tcp.Received>();
                    ExpectReceivedData(handler, remaining - recv.Data.Count);
                }
            }

            public IEnumerable<Inet.SocketOption> BindOptions { get; set; }
            public IEnumerable<Inet.SocketOption> ConnectOptions { get; set; }

            public IPEndPoint Endpoint { get { return _endpoint; } }

            public void Run(Action<TestSetup> action)
            {
                if (_shouldBindServer) BindServer();
                action(this);
            }
            
            public Task RunAsync(Func<TestSetup, Task> asyncAction)
            {
                if (_shouldBindServer) BindServer();
                return asyncAction(this);
            }
        }

    }
}
