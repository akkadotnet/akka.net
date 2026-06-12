//-----------------------------------------------------------------------
// <copyright file="TcpIntegrationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
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
using Akka.Util.Internal;
using Xunit;
using FluentAssertions;
using System.Runtime.InteropServices;
using Akka.Util;

namespace Akka.Tests.IO
{
    public class TcpIntegrationSpec : AkkaSpec
    {
        public const int InternalConnectionActorMaxQueueSize = 10000;

        class Aye : Tcp.Event { public static readonly Aye Instance = new(); }
        class Yes : Tcp.Event { public static readonly Yes Instance = new(); }
        class Ack : Tcp.Event { public static readonly Ack Instance = new(); }

        class AckWithValue : Tcp.Event
        {
            public object Value { get; }
            public static AckWithValue Create(object value) => new(value);
            private AckWithValue(object value) { Value = value; }
        }


        public TcpIntegrationSpec(ITestOutputHelper output)
            : base($@"akka.loglevel = DEBUG
                     akka.actor.serialize-creators = on
                     akka.actor.serialize-messages = on
                     akka.io.tcp.trace-logging = true
                     akka.io.tcp.write-commands-queue-max-size = {InternalConnectionActorMaxQueueSize}", output: output)
        { }

        private async Task VerifyActorTermination(IActorRef actor)
        {
            await WatchAsync(actor);
            await ExpectTerminatedAsync(actor);
        }

        [Fact]
        public async Task The_TCP_transport_implementation_should_properly_bind_a_test_server()
        {
            await new TestSetup(this).RunAsync(async _ => await Task.CompletedTask);
        }

        [Fact]
        public async Task The_TCP_transport_implementation_should_allow_connecting_to_and_disconnecting_from_the_test_server()
        {
            await new TestSetup(this).RunAsync(async x =>
            {
                var actors = await x.EstablishNewClientConnectionAsync();
                actors.ClientHandler.Send(actors.ClientConnection, Tcp.Close.Instance);
                await actors.ClientHandler.ExpectMsgAsync<Tcp.Closed>();

                await actors.ServerHandler.ExpectMsgAsync<Tcp.PeerClosed>();
                await VerifyActorTermination(actors.ClientConnection);
                await VerifyActorTermination(actors.ServerConnection);
            });
        }

        [Fact]
        public async Task The_TCP_transport_implementation_should_properly_handle_connection_abort_from_client_side()
        {
            await new TestSetup(this).RunAsync(async x =>
            {
                var actors = await x.EstablishNewClientConnectionAsync();
                actors.ClientHandler.Send(actors.ClientConnection, Tcp.Abort.Instance);
                await actors.ClientHandler.ExpectMsgAsync<Tcp.Aborted>();
                await actors.ServerHandler.ExpectMsgAsync<Tcp.ErrorClosed>();
                await VerifyActorTermination(actors.ClientConnection);
                await VerifyActorTermination(actors.ServerConnection);
            });
        }

        [Fact]
        public async Task The_TCP_transport_implementation_should_properly_handle_connection_abort_from_client_side_after_chit_chat()
        {
            await new TestSetup(this).RunAsync(async x =>
            {
                var actors = await x.EstablishNewClientConnectionAsync();
                await ChitChat(actors);

                actors.ClientHandler.Send(actors.ClientConnection, Tcp.Abort.Instance);
                await actors.ClientHandler.ExpectMsgAsync<Tcp.Aborted>();
                await actors.ServerHandler.ExpectMsgAsync<Tcp.ErrorClosed>();
                await VerifyActorTermination(actors.ClientConnection);
                await VerifyActorTermination(actors.ServerConnection);
            });
        }

        [Fact]
        public async Task The_TCP_transport_implementation_should_properly_handle_connection_abort_via_PoisonPill_from_client_side()
        {
            await new TestSetup(this).RunAsync(async x =>
            {
                var actors = await x.EstablishNewClientConnectionAsync();
                actors.ClientHandler.Send(actors.ClientConnection, PoisonPill.Instance);
                await VerifyActorTermination(actors.ClientConnection);

                await actors.ServerHandler.ExpectMsgAsync<Tcp.ErrorClosed>();
                await VerifyActorTermination(actors.ServerConnection);
            });
        }

        [Fact]
        public async Task The_TCP_transport_implementation_should_properly_handle_connection_abort_via_PoisonPill_from_client_side_after_chit_chat()
        {
            await new TestSetup(this).RunAsync(async x =>
            {
                var actors = await x.EstablishNewClientConnectionAsync();
                await ChitChat(actors);

                actors.ClientHandler.Send(actors.ClientConnection, PoisonPill.Instance);
                await VerifyActorTermination(actors.ClientConnection);

                await actors.ServerHandler.ExpectMsgAsync<Tcp.ErrorClosed>();
                await VerifyActorTermination(actors.ServerConnection);
            });
        }

        [Fact]
        public async Task The_TCP_transport_implementation_should_properly_handle_connection_abort_via_PoisonPill_from_server_side()
        {
            await new TestSetup(this).RunAsync(async x =>
            {
                var actors = await x.EstablishNewClientConnectionAsync();
                actors.ServerHandler.Send(actors.ServerConnection, PoisonPill.Instance);
                await VerifyActorTermination(actors.ServerConnection);

                await actors.ClientHandler.ExpectMsgAsync<Tcp.ErrorClosed>();
                await VerifyActorTermination(actors.ClientConnection);
            });
        }

        [Fact]
        public async Task The_TCP_transport_implementation_should_properly_handle_connection_abort_via_PoisonPill_from_server_side_after_chit_chat()
        {
            await new TestSetup(this).RunAsync(async x =>
            {
                var actors = await x.EstablishNewClientConnectionAsync();
                await ChitChat(actors);

                actors.ServerHandler.Send(actors.ServerConnection, PoisonPill.Instance);
                await VerifyActorTermination(actors.ServerConnection);

                await actors.ClientHandler.ExpectMsgAsync<Tcp.ErrorClosed>();
                await VerifyActorTermination(actors.ClientConnection);
            });
        }

        [InlineData(AddressFamily.InterNetwork)]
        [InlineData(AddressFamily.InterNetworkV6)]
        [Theory]
        public async Task The_TCP_transport_implementation_should_properly_support_connecting_to_DNS_endpoints(AddressFamily family)
        {
            if (family == AddressFamily.InterNetworkV6)
            {
                // Skip if "localhost" doesn't resolve to an IPv6 address on this system.
                // Socket.ConnectAsync(DnsEndPoint) only tries addresses returned by DNS —
                // if DNS doesn't return ::1, we can't reach an IPv6-only server via "localhost".
                // TODO: see https://github.com/akkadotnet/akka.net/issues/8178 — separate-PR DNS resolution issue may apply here.
                var addresses = await System.Net.Dns.GetHostAddressesAsync("localhost");
                if (!addresses.Any(a => a.AddressFamily == AddressFamily.InterNetworkV6))
                    return;
            }

            var serverHandler = CreateTestProbe();
            var bindCommander = CreateTestProbe();
            var loopback = family == AddressFamily.InterNetwork ? IPAddress.Loopback : IPAddress.IPv6Loopback;
            bindCommander.Send(Sys.Tcp(), new Tcp.Bind(serverHandler.Ref, new IPEndPoint(loopback, 0)));
            var boundMsg = await bindCommander.ExpectMsgAsync<Tcp.Bound>();

            // setup client to connect
            var targetAddress = new DnsEndPoint("localhost", boundMsg.LocalAddress.AsInstanceOf<IPEndPoint>().Port);
            var clientHandler = CreateTestProbe();
            Sys.Tcp().Tell(new Tcp.Connect(targetAddress), clientHandler);
            await clientHandler.ExpectMsgAsync<Tcp.Connected>(TimeSpan.FromSeconds(3));
            var clientEp = clientHandler.Sender;
            clientEp.Tell(new Tcp.Register(clientHandler));
            await serverHandler.ExpectMsgAsync<Tcp.Connected>();
            serverHandler.Sender.Tell(new Tcp.Register(serverHandler));

            var str = Enumerable.Repeat("f", 567).Join("");
            var testData = Encoding.ASCII.GetBytes(str).AsMemory();
            clientEp.Tell(Tcp.Write.Create(testData, Ack.Instance), clientHandler);
            await clientHandler.ExpectMsgAsync<Ack>();
            var received = await serverHandler
                .ReceiveWhileAsync(o => o as Tcp.Received,
                    RemainingOrDefault,
                    TimeSpan.FromSeconds(0.5)).ToListAsync();

            received.Sum(s => s.Data.Length).Should().Be(testData.Length);
        }

        [Fact]
        public async Task BugFix_3021_Tcp_Should_not_drop_large_messages()
        {
            await new TestSetup(this).RunAsync(async x =>
            {
                var actors = await x.EstablishNewClientConnectionAsync();

                // create a large-ish byte string
                const int length = 2064;
                var buffer1 = new byte[length];
                ThreadLocalRandom.Current.NextBytes(buffer1);

                var buffer2 = new byte[length];
                ThreadLocalRandom.Current.NextBytes(buffer2);

                var buffer3 = new byte[length];
                ThreadLocalRandom.Current.NextBytes(buffer3);

                // queue 3 writes
                actors.ClientHandler.Send(actors.ClientConnection, Tcp.Write.Create(buffer1.AsMemory()));
                actors.ClientHandler.Send(actors.ClientConnection, Tcp.Write.Create(buffer2.AsMemory()));
                actors.ClientHandler.Send(actors.ClientConnection, Tcp.Write.Create(buffer3.AsMemory()));

                var serverMsgs = await actors.ServerHandler.ReceiveWhileAsync(o =>
                {
                    return o as Tcp.Received;
                }, RemainingOrDefault, TimeSpan.FromSeconds(2)).ToListAsync();

                serverMsgs.Sum(s => s.Data.Length).Should().Be(length*3);

                // verify that the messages are in the same order as they were sent
                var bigBuffer = new byte[length * 3];
                var offset = 0;
                foreach (var serverMsg in serverMsgs)
                {
                    var dataLength = (int)serverMsg.Data.Length;
                    serverMsg.Data.CopyTo(bigBuffer.AsSpan(offset, dataLength));
                    offset += dataLength;
                }

                var recv1 = bigBuffer.Slice(0, length).ToArray();
                Assert.Equivalent(recv1, buffer1);

                var recv2 = bigBuffer.Slice(length, length).ToArray();
                Assert.Equivalent(recv2, buffer2);

                var recv3 = bigBuffer.Slice(length * 2, length).ToArray();
                Assert.Equivalent(recv3, buffer3);
            });
        }

        [Fact]
        public async Task When_sending_Close_to_TcpManager_Should_log_detailed_error_message()
        {
            await new TestSetup(this).RunAsync(async x =>
            {
                // Setup multiple clients
                var actors = await x.EstablishNewClientConnectionAsync();

                // Error message should contain invalid message type
                await EventFilter.Error(contains: nameof(Tcp.Close)).ExpectOneAsync(() => {
                    // Sending `Tcp.Close` to TcpManager instead of outgoing connection
                    Sys.Tcp().Tell(Tcp.Close.Instance, actors.ClientHandler);
                    return Task.CompletedTask;
                });
                // Should also contain ref to documentation
                await EventFilter.Error(contains: "https://getakka.net/articles/networking/io.html").ExpectOneAsync(() => {
                    // Sending `Tcp.Close` to TcpManager instead of outgoing connection
                    Sys.Tcp().Tell(Tcp.Close.Instance, actors.ClientHandler);
                    return Task.CompletedTask;
                });
            });
        }

        [Fact]
        public async Task Write_before_Register_should_not_be_silently_dropped()
        {
            await new TestSetup(this).RunAsync(async x =>
            {
                var actors = await x.EstablishNewClientConnectionAsync(registerClientHandler: false);

                var msg = Encoding.ASCII.GetBytes("msg").AsMemory(); // 3 bytes

                await EventFilter.Warning(new Regex("Received Write command before Register[^3]+3 bytes")).ExpectOneAsync(() => {
                    actors.ClientHandler.Send(actors.ClientConnection, Tcp.Write.Create(msg));
                    actors.ClientConnection.Tell(new Tcp.Register(actors.ClientHandler));
                    return Task.CompletedTask;
                });

                var serverMsgs = await actors.ServerHandler.ReceiveWhileAsync(o =>
                {
                    return o as Tcp.Received;
                }, RemainingOrDefault, TimeSpan.FromSeconds(2)).ToListAsync();

                var msgBytes = msg.ToArray();
                serverMsgs.Should().HaveCount(1).And.Subject.Should().Contain(m => m.Data.ToArray().SequenceEqual(msgBytes));
            });
        }

        [Fact]
        public async Task Write_before_Register_should_Be_dropped_if_buffer_is_full()
        {
            await new TestSetup(this).RunAsync(async x =>
            {
                var actors = await x.EstablishNewClientConnectionAsync(registerClientHandler: false);

                var overflowData = new byte[InternalConnectionActorMaxQueueSize + 1].AsMemory();

                // We do not want message about receiving Write to be logged, if the write was actually discarded
                await EventFilter.Warning(new Regex("Received Write command before Register[^3]+3 bytes")).ExpectAsync(0, () => {
                    actors.ClientHandler.Send(actors.ClientConnection, Tcp.Write.Create(overflowData));
                    return Task.CompletedTask;
                });

                await actors.ClientHandler.ExpectMsgAsync<Tcp.CommandFailed>(TimeSpan.FromSeconds(10));

                // After failed receive, next "good" writes should be handled with no issues
                actors.ClientHandler.Send(actors.ClientConnection, Tcp.Write.Create(new byte[1].AsMemory()));
                actors.ClientHandler.Send(actors.ClientConnection, new Tcp.Register(actors.ClientHandler));
                var serverMsgs = await actors.ServerHandler.ReceiveWhileAsync(o => o as Tcp.Received, RemainingOrDefault, TimeSpan.FromSeconds(2)).ToListAsync();
                serverMsgs.Should().HaveCount(1).And.Subject.Should().Contain(m => m.Data.Length == 1);
            });
        }

        [Fact]
        public async Task When_multiple_concurrent_writing_clients_Should_not_lose_messages()
        {
            const int clientsCount = 50;

            await new TestSetup(this).RunAsync(async x =>
            {
                // Setup multiple clients
                var actors = await x.EstablishNewClientConnectionAsync();

                // Each client sends his index to server
                var clients = Enumerable.Range(0, clientsCount).Select(i => (Index: i, Probe: CreateTestProbe($"test-client-{i}"))).ToArray();
                var counter = new AtomicCounter(0);
                Parallel.ForEach(clients, client =>
                {
                    var msg = Encoding.ASCII.GetBytes(client.Index.ToString()).AsMemory();
                    counter.AddAndGet(msg.Length);
                    client.Probe.Send(actors.ClientConnection, Tcp.Write.Create(msg));
                });

                // All messages data should be received
                var received = await actors.ServerHandler.ReceiveWhileAsync(o => o as Tcp.Received, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1.5)).ToListAsync();
                received.Sum(r => r.Data.Length).ShouldBe(counter.Current);
            });
        }

        [Fact]
        public async Task When_multiple_concurrent_writing_clients_All_acks_should_be_received()
        {
            const int clientsCount = 50;

            await new TestSetup(this).RunAsync(async x =>
            {
                // Setup multiple clients
                var actors = await x.EstablishNewClientConnectionAsync();

                // Each client sends their index to server
                var indexRange = Enumerable.Range(0, clientsCount).ToList();
                var clients = indexRange.Select(i => (Index: i, Probe: CreateTestProbe($"test-client-{i}"))).ToArray();
                Parallel.ForEach(clients, client =>
                {
                    var msg = new byte[]{0}.AsMemory();
                    client.Probe.Send(actors.ClientConnection, Tcp.Write.Create(msg, AckWithValue.Create(client.Index)));
                });

                // All acks should be received
                foreach(var client in clients)
                {
                    await client.Probe.ExpectMsgAsync<AckWithValue>(ack => ack.Value.ShouldBe(client.Index), TimeSpan.FromSeconds(10));
                }
            });
        }

        [Fact]
        public async Task When_multiple_writing_clients_Should_receive_messages_in_order()
        {
            const int clientsCount = 50;

            await new TestSetup(this).RunAsync(async x =>
            {
                // Setup multiple clients
                var actors = await x.EstablishNewClientConnectionAsync();

                // Each client sends their index to server
                var clients = Enumerable.Range(0, clientsCount).Select(i => (Index: i, Probe: CreateTestProbe($"test-client-{i}"))).ToArray();
                var contentBuilder = new StringBuilder();
                clients.ForEach(client =>
                {
                    var msg = client.Index.ToString();
                    contentBuilder.Append(msg);
                    client.Probe.Send(actors.ClientConnection, Tcp.Write.Create(Encoding.ASCII.GetBytes(msg).AsMemory()));
                });

                // All messages data should be received, and be in the same order as they were sent
                var received = await actors.ServerHandler.ReceiveWhileAsync(o => o as Tcp.Received, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1.5)).ToListAsync();
                var content = string.Join("", received.Select(r => Encoding.ASCII.GetString(r.Data.ToArray())));
                content.ShouldBe(contentBuilder.ToString());
            });
        }

         [Fact]
        public async Task Should_fail_writing_when_buffer_is_filled()
        {
            await new TestSetup(this).RunAsync(async x =>
            {
                var actors = await x.EstablishNewClientConnectionAsync();

                // create a buffer-overflow message
                var overflowData = new byte[InternalConnectionActorMaxQueueSize + 1].AsMemory();
                var goodData = new byte[InternalConnectionActorMaxQueueSize].AsMemory();

                // If test runner is too loaded, let it try ~3 times with 5 pause interval
                await AwaitAssertAsync(async () =>
                {
                    // try sending overflow
                    actors.ClientHandler.Send(actors.ClientConnection, Tcp.Write.Create(goodData)); // this is sent immediately
                    actors.ClientHandler.Send(actors.ClientConnection, Tcp.Write.Create(overflowData)); // this will fail
                    await actors.ClientHandler.ExpectMsgAsync<Tcp.CommandFailed>();

                    // First message will go through, second one will not
                    (await actors.ServerHandler.ReceiveWhileAsync(TimeSpan.FromSeconds(1), m => m as Tcp.Received).ToListAsync())
                        .Sum(m => m.Data.Length)
                        .Should().Be(InternalConnectionActorMaxQueueSize);

                    // Check that almost-overflow size does not cause any problems
                    //actors.ClientHandler.Send(actors.ClientConnection, Tcp.ResumeWriting.Instance); // Recover after send failure
                    actors.ClientHandler.Send(actors.ClientConnection, Tcp.Write.Create(goodData));
                    (await actors.ServerHandler.ReceiveWhileAsync(TimeSpan.FromSeconds(1), m => m as Tcp.Received).ToListAsync())
                        .Sum(m => m.Data.Length)
                        .Should().Be(InternalConnectionActorMaxQueueSize);
                }, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(3)); // 3 attempts by ~25 seconds + 5 sec pause
            });
        }


        [Fact]
        public async Task The_TCP_transport_implementation_should_properly_complete_one_client_server_request_response_cycle()
        {
            await new TestSetup(this).RunAsync(async x =>
            {
                var actors = await x.EstablishNewClientConnectionAsync();

                actors.ClientHandler.Send(actors.ClientConnection, Tcp.Write.Create(Encoding.ASCII.GetBytes("Captain on the bridge!").AsMemory(), Aye.Instance));
                await actors.ClientHandler.ExpectMsgAsync(Aye.Instance);
                Encoding.ASCII.GetString((await actors.ServerHandler.ExpectMsgAsync<Tcp.Received>()).Data.ToArray()).ShouldBe("Captain on the bridge!");

                actors.ServerHandler.Send(actors.ServerConnection, Tcp.Write.Create(Encoding.ASCII.GetBytes("For the king!").AsMemory(), Yes.Instance));
                await actors.ServerHandler.ExpectMsgAsync(Yes.Instance);
                Encoding.ASCII.GetString((await actors.ClientHandler.ExpectMsgAsync<Tcp.Received>()).Data.ToArray()).ShouldBe("For the king!");

                actors.ServerHandler.Send(actors.ServerConnection, Tcp.Close.Instance);
                await actors.ServerHandler.ExpectMsgAsync<Tcp.Closed>();
                await actors.ClientHandler.ExpectMsgAsync<Tcp.PeerClosed>();

                await VerifyActorTermination(actors.ClientConnection);
                await VerifyActorTermination(actors.ServerConnection);
            });
        }


        [Fact]
        public async Task The_TCP_transport_implementation_should_support_waiting_for_writes_with_backpressure()
        {
            var transmittedBytes = InternalConnectionActorMaxQueueSize;
            await new TestSetup(this).RunAsync(async x =>
            {
                x.BindOptions = [new Inet.SO.SendBufferSize(1024)];
                x.ConnectOptions = [new Inet.SO.SendBufferSize(1024)];

                var actors = await x.EstablishNewClientConnectionAsync();

                actors.ServerHandler.Send(actors.ServerConnection, Tcp.Write.Create(new byte[transmittedBytes].AsMemory(), Ack.Instance));
                await actors.ServerHandler.ExpectMsgAsync(Ack.Instance);

                await x.ExpectReceivedDataAsync(actors.ClientHandler, transmittedBytes);
            });
        }

        [Fact]
        public async Task The_TCP_transport_implementation_dont_report_Connected_when_endpoint_isnt_responding()
        {
            var connectCommander = CreateTestProbe();
            // a "random" endpoint hopefully unavailable since it's in the test-net IP range
            var endpoint = new IPEndPoint(IPAddress.Parse("192.0.2.1"), 23825);
            connectCommander.Send(Sys.Tcp(), new Tcp.Connect(endpoint));
            // expecting CommandFailed or no reply (within timeout)
            var replies = await connectCommander.ReceiveWhileAsync(TimeSpan.FromSeconds(1), x => x as Tcp.Connected).ToListAsync();
            replies.Count.ShouldBe(0);
        }

        [Fact]
        public async Task Should_report_Error_only_once_when_connecting_to_unreachable_DnsEndpoint()
        {
            var probe = CreateTestProbe();
            var endpoint = new DnsEndPoint("fake", 1000);
            // Use a connect timeout so we don't depend on platform-specific DNS timeout behavior.
            // Windows DNS resolver can take 10-15+ seconds to fail for unresolvable hosts,
            // so we need a generous ExpectMsg timeout beyond the connect timeout.
            Sys.Tcp().Tell(new Tcp.Connect(endpoint, timeout: TimeSpan.FromSeconds(5)), probe.Ref);

            var reply = await probe.ExpectMsgAsync<Tcp.CommandFailed>(TimeSpan.FromSeconds(30));
            reply.Should().NotBeNull();
        }

        [Fact]
        public async Task Should_report_CommandFailed_when_outgoing_connection_is_refused()
        {
            // Regression guard for https://github.com/akkadotnet/akka.net/issues/8195
            //
            // When a connect attempt fails, TcpOutgoingConnection schedules a retry. The retry
            // used to run on the scheduler thread, so any exception it threw (e.g.
            // PlatformNotSupportedException on Linux when reusing a socket after a failed
            // connect) was logged and swallowed by the scheduler - the commander was never
            // told and stayed stuck forever. The retry now runs inside the actor's message
            // loop, so any such failure is surfaced to the commander as Tcp.CommandFailed.
            //
            // The commander must always end up receiving CommandFailed rather than hanging.
            var connectCommander = CreateTestProbe();

            // Bind a socket to grab a free loopback port, then release it so the
            // connection attempt is refused.
            int port;
            using (var probeSocket = new Socket(SocketType.Stream, ProtocolType.Tcp))
            {
                probeSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                port = ((IPEndPoint)probeSocket.LocalEndPoint).Port;
            }

            // A connect timeout makes the outcome deterministic across platforms: on Linux the
            // refused connection fails fast and drives the retry path, while on platforms where
            // connecting to an unused port does not fail fast (e.g. Windows), the connect-timeout
            // ReceiveTimeout still guarantees CommandFailed is delivered.
            connectCommander.Send(Sys.Tcp(),
                new Tcp.Connect(new IPEndPoint(IPAddress.Loopback, port), timeout: TimeSpan.FromSeconds(3)));

            await connectCommander.ExpectMsgAsync<Tcp.CommandFailed>(TimeSpan.FromSeconds(20));
        }

        [Fact]
        public async Task The_TCP_transport_implementation_handle_tcp_connection_actor_death_properly()
        {
            await new TestSetup(this, shouldBindServer:false).RunAsync(async _ =>
            {
                var serverSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                serverSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                serverSocket.Listen(100);
                var endpoint = (IPEndPoint) serverSocket.LocalEndPoint;

                var connectCommander = CreateTestProbe();
                connectCommander.Send(Sys.Tcp(), new Tcp.Connect(endpoint));

                var accept = serverSocket.Accept();
                var connected = await connectCommander.ExpectMsgAsync<Tcp.Connected>();
                connected.RemoteAddress.AsInstanceOf<IPEndPoint>().Port.ShouldBe(endpoint.Port);
                var connectionActor = connectCommander.LastSender;
                connectCommander.Send(connectionActor, PoisonPill.Instance);

                await AwaitConditionNoThrowAsync(() =>
                {
                    try
                    {
                        var buffer = new byte[1024];
                        return Task.FromResult(accept.Receive(buffer) == 0);
                    }
                    catch (SocketException)
                    {
                        return Task.FromResult(true);
                    }
                }, TimeSpan.FromSeconds(3));

                await VerifyActorTermination(connectionActor);
            });
        }

        private async Task ChitChat(TestSetup.ConnectionDetail actors, int rounds = 100)
        {
            var testData = new byte[]{0}.AsMemory();
            for (var i = 0; i < rounds; i++)
            {
                actors.ClientHandler.Send(actors.ClientConnection, Tcp.Write.Create(testData));
                await actors.ServerHandler.ExpectMsgAsync<Tcp.Received>(x => x.Data.Length == 1 && x.Data.FirstSpan[0] == 0, hint: $"server didn't received at {i} round");
                actors.ServerHandler.Send(actors.ServerConnection, Tcp.Write.Create(testData));
                await actors.ClientHandler.ExpectMsgAsync<Tcp.Received>(x => x.Data.Length == 1 && x.Data.FirstSpan[0] == 0, hint: $"client didn't received at {i} round");
            }
        }

        class TestSetup
        {
            private readonly AkkaSpec _spec;
            private readonly bool _shouldBindServer;
            private readonly TestProbe _bindHandler;
            private IPEndPoint _endpoint;
            private readonly TcpSettings _settings;

            public TestSetup(AkkaSpec spec, bool shouldBindServer = true, TcpSettings? settings = null)
            {
                _settings = settings ?? TcpSettings.Create(spec.Sys);
                BindOptions =  [];
                ConnectOptions = [];
                _spec = spec;
                _shouldBindServer = shouldBindServer;
                _bindHandler = _spec.CreateTestProbe("bind-handler-probe");
            }

            public async Task BindServer()
            {
                var bindCommander = _spec.CreateTestProbe();
                bindCommander.Send(_spec.Sys.Tcp(),
                    new Tcp.Bind(_bindHandler.Ref, new IPEndPoint(IPAddress.Loopback, 0), options: BindOptions){ TcpSettings = _settings});
                await bindCommander.ExpectMsgAsync<Tcp.Bound>(bound => _endpoint = (IPEndPoint) bound.LocalAddress);
            }

            public async Task<ConnectionDetail> EstablishNewClientConnectionAsync(bool registerClientHandler = true, TcpSettings? settings = null)
            {
                var connectCommander = _spec.CreateTestProbe("connect-commander-probe");
                connectCommander.Send(_spec.Sys.Tcp(), new Tcp.Connect(_endpoint, options: ConnectOptions){ TcpSettings = settings ?? _settings});
                await connectCommander.ExpectMsgAsync<Tcp.Connected>();

                var clientHandler = _spec.CreateTestProbe("client-handler-probe");
                if (registerClientHandler)
                    connectCommander.Sender.Tell(new Tcp.Register(clientHandler.Ref));

                await _bindHandler.ExpectMsgAsync<Tcp.Connected>();
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

            public async Task ExpectReceivedDataAsync(TestProbe handler, int remaining)
            {
                while (true)
                {
                    if (remaining > 0)
                    {
                        var recv = await handler.ExpectMsgAsync<Tcp.Received>();
                        remaining = remaining - (int)recv.Data.Length;
                        continue;
                    }

                    break;
                }
            }

            public IEnumerable<Inet.SocketOption> BindOptions { get; set; }
            public IEnumerable<Inet.SocketOption> ConnectOptions { get; set; }

            public IPEndPoint Endpoint { get { return _endpoint; } }

            public async Task RunAsync(Func<TestSetup, Task> asyncAction)
            {
                if (_shouldBindServer) await BindServer();
                await asyncAction(this);
            }
        }

    }
}
