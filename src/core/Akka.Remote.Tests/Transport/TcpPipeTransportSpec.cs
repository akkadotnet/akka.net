//-----------------------------------------------------------------------
// <copyright file="TcpPipeTransportSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Transport;
using Akka.Remote.Transport.Pipelines;
using Akka.TestKit;
using FluentAssertions;
using Google.Protobuf;
using Xunit;
using Xunit.v3;

namespace Akka.Remote.Tests.Transport
{
    /// <summary>
    /// Integration tests for <see cref="TcpPipeTransport"/>.
    ///
    /// Each test creates real TCP sockets on 127.0.0.1 with port=0 (OS-assigned)
    /// to keep tests fast, deterministic, and independent of each other.
    ///
    /// <!-- CopilotNotes: The pattern mirrors DotNettyTransportShutdownSpec:
    ///      - Create two TcpPipeTransport instances sharing a config with port=0.
    ///      - Call Listen() on both, complete their association-listener promises.
    ///      - Drive the returned AssociationHandles directly (no AkkaProtocol layer)
    ///        so we test only the transport's wire behaviour.
    ///      - Always call Shutdown() in a finally block to free the sockets. -->
    /// </summary>
    public class TcpPipeTransportSpec : AkkaSpec
    {
        // ── HOCON ──────────────────────────────────────────────────────────────
        private static readonly Config TestConfig = ConfigurationFactory.ParseString(@"
            akka {
                loglevel = DEBUG
                actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                remote {
                    pipe.tcp {
                        port     = 0
                        hostname = ""127.0.0.1""
                    }
                }
            }");

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>Create a fresh transport using the test config.</summary>
        private TcpPipeTransport NewTransport() =>
            new(Sys, Sys.Settings.Config.GetConfig("akka.remote.pipe.tcp"));

        // ── Constructor ────────────────────────────────────────────────────────

        public TcpPipeTransportSpec(ITestOutputHelper output)
            : base(TestConfig, output) { }

        // ── Tests ──────────────────────────────────────────────────────────────

        [Fact(DisplayName = "TcpPipeTransport_Should_Bind_And_Return_Address_When_Listen_Called")]
        public async Task TcpPipeTransport_Should_Bind_And_Return_Address_When_Listen_Called()
        {
            var t1 = NewTransport();
            try
            {
                var (addr, listenerPromise) = await t1.Listen();

                addr.Should().NotBeNull();
                addr.Host.Should().Be("127.0.0.1");
                addr.Port.Should().BeGreaterThan(0);
                addr.Protocol.Should().Be("tcp");
                listenerPromise.Should().NotBeNull();
            }
            finally
            {
                await t1.Shutdown();
            }
        }

        [Fact(DisplayName = "TcpPipeTransport_Should_Associate_Outbound_And_Deliver_InboundAssociation")]
        public async Task TcpPipeTransport_Should_Associate_Outbound_And_Deliver_InboundAssociation()
        {
            var t1 = NewTransport();
            var t2 = NewTransport();
            try
            {
                var p1 = CreateTestProbe();
                var p2 = CreateTestProbe();

                var (addr1, lp1) = await t1.Listen();
                lp1.SetResult(new ActorAssociationEventListener(p1));

                var (addr2, lp2) = await t2.Listen();
                lp2.SetResult(new ActorAssociationEventListener(p2));

                // Outbound: t1 ──► t2
                var outboundHandle = await t1.Associate(addr2);
                outboundHandle.Should().NotBeNull();
                outboundHandle.RemoteAddress.Should().Be(addr2);

                // t2 should receive InboundAssociation from t1
                var inbound = await p2.ExpectMsgAsync<InboundAssociation>(TimeSpan.FromSeconds(5));
                inbound.Association.Should().NotBeNull();
                inbound.Association.RemoteAddress.Host.Should().Be("127.0.0.1");
            }
            finally
            {
                await t1.Shutdown();
                await t2.Shutdown();
            }
        }

        [Fact(DisplayName = "TcpPipeTransport_Should_Deliver_Payload_From_Outbound_To_Inbound")]
        public async Task TcpPipeTransport_Should_Deliver_Payload_From_Outbound_To_Inbound()
        {
            var t1 = NewTransport();
            var t2 = NewTransport();
            try
            {
                var p1 = CreateTestProbe();
                var p2 = CreateTestProbe();

                var (addr1, lp1) = await t1.Listen();
                lp1.SetResult(new ActorAssociationEventListener(p1));

                var (addr2, lp2) = await t2.Listen();
                lp2.SetResult(new ActorAssociationEventListener(p2));

                // Establish association
                var outHandle = await t1.Associate(addr2);
                var inbound   = await p2.ExpectMsgAsync<InboundAssociation>(TimeSpan.FromSeconds(5));
                var inHandle  = inbound.Association;

                // Register listeners so reads are routed to the probes
                outHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(p1));
                inHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(p2));

                // Send a payload t1 ──► t2
                var payload = ByteString.CopyFromUtf8("Hello from Ami-chan!");
                var wrote   = outHandle.Write(payload);
                wrote.Should().BeTrue();

                // t2's probe should receive the InboundPayload
                var received = await p2.ExpectMsgAsync<InboundPayload>(TimeSpan.FromSeconds(5));
                received.Payload.ToStringUtf8().Should().Be("Hello from Ami-chan!");
            }
            finally
            {
                await t1.Shutdown();
                await t2.Shutdown();
            }
        }

        [Fact(DisplayName = "TcpPipeTransport_Should_Deliver_Payload_From_Inbound_To_Outbound")]
        public async Task TcpPipeTransport_Should_Deliver_Payload_From_Inbound_To_Outbound()
        {
            var t1 = NewTransport();
            var t2 = NewTransport();
            try
            {
                var p1 = CreateTestProbe();
                var p2 = CreateTestProbe();

                var (addr1, lp1) = await t1.Listen();
                lp1.SetResult(new ActorAssociationEventListener(p1));

                var (addr2, lp2) = await t2.Listen();
                lp2.SetResult(new ActorAssociationEventListener(p2));

                var outHandle = await t1.Associate(addr2);
                var inbound   = await p2.ExpectMsgAsync<InboundAssociation>(TimeSpan.FromSeconds(5));
                var inHandle  = inbound.Association;

                outHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(p1));
                inHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(p2));

                // Send the OTHER direction: t2 ──► t1
                var payload = ByteString.CopyFromUtf8("Reply from t2 uwu~");
                inHandle.Write(payload);

                var received = await p1.ExpectMsgAsync<InboundPayload>(TimeSpan.FromSeconds(5));
                received.Payload.ToStringUtf8().Should().Be("Reply from t2 uwu~");
            }
            finally
            {
                await t1.Shutdown();
                await t2.Shutdown();
            }
        }

        [Fact(DisplayName = "TcpPipeTransport_Should_Send_Multiple_Payloads_In_Order")]
        public async Task TcpPipeTransport_Should_Send_Multiple_Payloads_In_Order()
        {
            var t1 = NewTransport();
            var t2 = NewTransport();
            try
            {
                var p1 = CreateTestProbe();
                var p2 = CreateTestProbe();

                var (_, lp1) = await t1.Listen();
                lp1.SetResult(new ActorAssociationEventListener(p1));

                var (addr2, lp2) = await t2.Listen();
                lp2.SetResult(new ActorAssociationEventListener(p2));

                var outHandle = await t1.Associate(addr2);
                var inbound   = await p2.ExpectMsgAsync<InboundAssociation>(TimeSpan.FromSeconds(5));
                var inHandle  = inbound.Association;

                outHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(p1));
                inHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(p2));

                // Write 5 messages back-to-back to exercise write coalescing
                // CopilotNotes: This exercises the ArrayBufferWriter batch-drain path in
                // PipeConnection.WriteLoopAsync — all 5 writes may land in a single TCP segment.
                const int MessageCount = 5;
                for (var i = 0; i < MessageCount; i++)
                    outHandle.Write(ByteString.CopyFromUtf8($"msg-{i}"));

                // All 5 must arrive in order at t2
                for (var i = 0; i < MessageCount; i++)
                {
                    var msg = await p2.ExpectMsgAsync<InboundPayload>(TimeSpan.FromSeconds(5));
                    msg.Payload.ToStringUtf8().Should().Be($"msg-{i}");
                }
            }
            finally
            {
                await t1.Shutdown();
                await t2.Shutdown();
            }
        }

        [Fact(DisplayName = "TcpPipeTransport_Should_Notify_Disassociated_When_Outbound_Calls_Disassociate")]
        public async Task TcpPipeTransport_Should_Notify_Disassociated_When_Outbound_Calls_Disassociate()
        {
            var t1 = NewTransport();
            var t2 = NewTransport();
            try
            {
                var p1 = CreateTestProbe();
                var p2 = CreateTestProbe();

                var (_, lp1) = await t1.Listen();
                lp1.SetResult(new ActorAssociationEventListener(p1));

                var (addr2, lp2) = await t2.Listen();
                lp2.SetResult(new ActorAssociationEventListener(p2));

                var outHandle = await t1.Associate(addr2);
                var inbound   = await p2.ExpectMsgAsync<InboundAssociation>(TimeSpan.FromSeconds(5));
                var inHandle  = inbound.Association;

                outHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(p1));
                inHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(p2));

                // Outbound side initiates disassociation
                outHandle.Disassociate("test disassociation", Log);

                // t2 (inbound) should receive the Disassociated notification
                var disassociated = await p2.ExpectMsgAsync<Disassociated>(TimeSpan.FromSeconds(5));
                disassociated.Should().NotBeNull();
            }
            finally
            {
                await t1.Shutdown();
                await t2.Shutdown();
            }
        }

        [Fact(DisplayName = "TcpPipeTransport_Should_Notify_Disassociated_When_Inbound_Calls_Disassociate")]
        public async Task TcpPipeTransport_Should_Notify_Disassociated_When_Inbound_Calls_Disassociate()
        {
            var t1 = NewTransport();
            var t2 = NewTransport();
            try
            {
                var p1 = CreateTestProbe();
                var p2 = CreateTestProbe();

                var (_, lp1) = await t1.Listen();
                lp1.SetResult(new ActorAssociationEventListener(p1));

                var (addr2, lp2) = await t2.Listen();
                lp2.SetResult(new ActorAssociationEventListener(p2));

                var outHandle = await t1.Associate(addr2);
                var inbound   = await p2.ExpectMsgAsync<InboundAssociation>(TimeSpan.FromSeconds(5));
                var inHandle  = inbound.Association;

                outHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(p1));
                inHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(p2));

                // Inbound side initiates disassociation
                inHandle.Disassociate("inbound test", Log);

                // t1 (outbound) must receive a Disassociated event
                var disassociated = await p1.ExpectMsgAsync<Disassociated>(TimeSpan.FromSeconds(5));
                disassociated.Should().NotBeNull();
            }
            finally
            {
                await t1.Shutdown();
                await t2.Shutdown();
            }
        }

        [Fact(DisplayName = "TcpPipeTransport_Should_Notify_Disassociated_On_Remote_Shutdown")]
        public async Task TcpPipeTransport_Should_Notify_Disassociated_On_Remote_Shutdown()
        {
            var t1 = NewTransport();
            var t2 = NewTransport();
            try
            {
                var p1 = CreateTestProbe();
                var p2 = CreateTestProbe();

                var (_, lp1) = await t1.Listen();
                lp1.SetResult(new ActorAssociationEventListener(p1));

                var (addr2, lp2) = await t2.Listen();
                lp2.SetResult(new ActorAssociationEventListener(p2));

                var outHandle = await t1.Associate(addr2);
                var inbound   = await p2.ExpectMsgAsync<InboundAssociation>(TimeSpan.FromSeconds(5));
                var inHandle  = inbound.Association;

                outHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(p1));
                inHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(p2));

                // Shut down t2 entirely — t1 should detect the connection is gone
                await t2.Shutdown();

                // t1 should receive Disassociated because t2's socket was closed
                await p1.ExpectMsgAsync<Disassociated>(TimeSpan.FromSeconds(10));
            }
            finally
            {
                await t1.Shutdown();
                // t2 already shut down, second call is safe
                await t2.Shutdown();
            }
        }

        [Fact(DisplayName = "TcpPipeTransport_Should_Throw_InvalidAssociationException_For_Refused_Connection")]
        public async Task TcpPipeTransport_Should_Throw_InvalidAssociationException_For_Refused_Connection()
        {
            var t1 = NewTransport();
            try
            {
                var (addr1, lp1) = await t1.Listen();
                lp1.SetResult(new ActorAssociationEventListener(CreateTestProbe()));

                // Obtain a guaranteed-free port by binding briefly then releasing.
                // CopilotNotes: The brief race window (bind→close→connect) is acceptable in
                // tests because the OS won't immediately reassign a just-released ephemeral port.
                int deadPort;
                using (var tmp = new System.Net.Sockets.Socket(
                           System.Net.Sockets.AddressFamily.InterNetwork,
                           System.Net.Sockets.SocketType.Stream,
                           System.Net.Sockets.ProtocolType.Tcp))
                {
                    tmp.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
                    deadPort = ((System.Net.IPEndPoint)tmp.LocalEndPoint!).Port;
                } // Socket closed here — nothing is listening on deadPort

                var deadAddress = addr1.WithPort(deadPort);

                await Assert.ThrowsAsync<InvalidAssociationException>(
                    () => t1.Associate(deadAddress));
            }
            finally
            {
                await t1.Shutdown();
            }
        }

        [Fact(DisplayName = "TcpPipeTransport_Should_Cleanly_Shutdown_Without_Active_Connections")]
        public async Task TcpPipeTransport_Should_Cleanly_Shutdown_Without_Active_Connections()
        {
            var t1 = NewTransport();

            await t1.Listen();

            // Shutdown with no active connections should succeed immediately
            var result = await t1.Shutdown().WaitAsync(TimeSpan.FromSeconds(5));
            result.Should().BeTrue();
        }

        [Fact(DisplayName = "TcpPipeTransport_Should_Return_False_For_Write_After_Disassociate")]
        public async Task TcpPipeTransport_Should_Return_False_For_Write_After_Disassociate()
        {
            var t1 = NewTransport();
            var t2 = NewTransport();
            try
            {
                var p1 = CreateTestProbe();
                var p2 = CreateTestProbe();

                var (_, lp1) = await t1.Listen();
                lp1.SetResult(new ActorAssociationEventListener(p1));

                var (addr2, lp2) = await t2.Listen();
                lp2.SetResult(new ActorAssociationEventListener(p2));

                var outHandle = await t1.Associate(addr2);
                var inbound   = await p2.ExpectMsgAsync<InboundAssociation>(TimeSpan.FromSeconds(5));

                outHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(p1));
                inbound.Association.ReadHandlerSource.SetResult(new ActorHandleEventListener(p2));

                // Disassociate then immediately try to write — should return false
                outHandle.Disassociate("test", Log);

                // Give the close a moment to propagate
                await Task.Delay(100);

                var writeResult = outHandle.Write(ByteString.CopyFromUtf8("should be dropped"));
                writeResult.Should().BeFalse("because the handle is already disassociated");
            }
            finally
            {
                await t1.Shutdown();
                await t2.Shutdown();
            }
        }

        [Fact(DisplayName = "TcpPipeTransport_Should_Send_And_Receive_Large_Payload")]
        public async Task TcpPipeTransport_Should_Send_And_Receive_Large_Payload()
        {
            var t1 = NewTransport();
            var t2 = NewTransport();
            try
            {
                var p1 = CreateTestProbe();
                var p2 = CreateTestProbe();

                var (_, lp1) = await t1.Listen();
                lp1.SetResult(new ActorAssociationEventListener(p1));

                var (addr2, lp2) = await t2.Listen();
                lp2.SetResult(new ActorAssociationEventListener(p2));

                var outHandle = await t1.Associate(addr2);
                var inbound   = await p2.ExpectMsgAsync<InboundAssociation>(TimeSpan.FromSeconds(5));
                var inHandle  = inbound.Association;

                outHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(p1));
                inHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(p2));

                // 64 KB payload — exercises multi-segment PipeReader reads
                var largeBytes = new byte[64 * 1024];
                new Random(42).NextBytes(largeBytes);
                var payload = ByteString.CopyFrom(largeBytes);

                outHandle.Write(payload);

                var received = await p2.ExpectMsgAsync<InboundPayload>(TimeSpan.FromSeconds(10));
                received.Payload.ToByteArray().Should().Equal(largeBytes);
            }
            finally
            {
                await t1.Shutdown();
                await t2.Shutdown();
            }
        }

        [Fact(DisplayName = "TcpPipeTransport_Should_Handle_Concurrent_Associates")]
        public async Task TcpPipeTransport_Should_Handle_Concurrent_Associates()
        {
            // Spin up 5 client transports all connecting to one server transport concurrently.
            const int ClientCount = 5;
            var server = NewTransport();
            var clients = Enumerable.Range(0, ClientCount).Select(_ => NewTransport()).ToArray();
            try
            {
                var serverProbe = CreateTestProbe();
                var (serverAddr, serverLp) = await server.Listen();
                serverLp.SetResult(new ActorAssociationEventListener(serverProbe));

                // Bind all clients
                var clientListens = await Task.WhenAll(clients.Select(c => c.Listen()));
                foreach (var (_, lp) in clientListens)
                    lp.SetResult(new ActorAssociationEventListener(CreateTestProbe()));

                // Connect all clients concurrently
                var connectTasks = clients.Select(c => c.Associate(serverAddr)).ToArray();
                var handles = await Task.WhenAll(connectTasks);

                // Server should receive one InboundAssociation per client
                for (var i = 0; i < ClientCount; i++)
                    await serverProbe.ExpectMsgAsync<InboundAssociation>(TimeSpan.FromSeconds(5));

                handles.Should().HaveCount(ClientCount);
                handles.Should().OnlyContain(h => h != null);
            }
            finally
            {
                await server.Shutdown();
                await Task.WhenAll(clients.Select(c => c.Shutdown()));
            }
        }
    }
}






