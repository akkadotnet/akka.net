//-----------------------------------------------------------------------
// <copyright file="DotNettyGracefulCloseSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Transport;
using Akka.Remote.Transport.DotNetty;
using Akka.TestKit;
using DotNetty.Transport.Channels;
using FluentAssertions;
using Google.Protobuf;
using Xunit;

namespace Akka.Remote.Tests.Transport
{
    /// <summary>
    /// A graceful disassociate must not reset the connection, or a Windows peer drops our last frames (#8589).
    /// </summary>
    public class DotNettyGracefulCloseSpec : AkkaSpec
    {
        private static readonly Config Config = ConfigurationFactory.ParseString(@"
            akka {
                loglevel = INFO
                actor.provider = remote
                remote.dot-netty.tcp {
                    port = 0
                    hostname = ""127.0.0.1""
                }
            }");

        private static readonly Config ShortFlushWait =
            ConfigurationFactory.ParseString("akka.remote.flush-wait-on-shutdown = 300ms").WithFallback(Config);

        private static readonly Config TlsOverrides = ConfigurationFactory.ParseString(@"
            enable-ssl = true
            ssl {
                suppress-validation = true
                require-mutual-authentication = false
                certificate {
                    path = ""Resources/akka-validcert.pfx""
                    password = ""password""
                }
            }");

        private static readonly ByteString Payload = ByteString.CopyFrom(Enumerable.Repeat((byte)7, 512).ToArray());

        public DotNettyGracefulCloseSpec(ITestOutputHelper output) : base(Config, output)
        {
        }

        [Fact(DisplayName = "Should_deliver_last_frame_and_clean_EOF_When_disassociating_with_unread_inbound_data")]
        public async Task Should_deliver_last_frame_and_clean_EOF_When_disassociating_with_unread_inbound_data()
        {
            var c = await ConnectRawPeer(Sys);
            try
            {
                // we never read these bytes; zeros decode as empty frames if we do
                c.Peer.Send(new byte[1024]);

                c.Handle.Write(Payload).Should().BeTrue();
                c.Handle.Disassociate("test", Log);

                (await ReadToEnd(new NetworkStream(c.Peer))).Should().Be(4 + Payload.Length);
                await Task.Delay(200);
                SocketError(c.Peer).Should().Be(0, "a reset makes a Windows peer drop the frames it has not read yet");

                // our side closes once it reads the peer's FIN, well before the 2 s flush-wait deadline
                c.Peer.Close();
                await c.Channel.CloseCompletion.WaitAsync(TimeSpan.FromSeconds(1));
            }
            finally
            {
                c.Peer.Dispose();
                await c.Transport.Shutdown();
            }
        }

        [Fact(DisplayName = "Should_deliver_last_frame_without_reset_When_TLS_is_enabled")]
        public async Task Should_deliver_last_frame_without_reset_When_TLS_is_enabled()
        {
            var c = await ConnectRawPeer(Sys, TlsOverrides.WithFallback(Sys.Settings.Config.GetConfig("akka.remote.dot-netty.tcp")));
            try
            {
                // the server side needs reads on to finish the handshake
                c.Handle.ReadHandlerSource.SetResult(new ActorHandleEventListener(CreateTestProbe()));
                await using var tls = new SslStream(new NetworkStream(c.Peer), false, (_, _, _, _) => true);
                await tls.AuthenticateAsClientAsync("localhost").WaitAsync(TimeSpan.FromSeconds(5));

                c.Handle.Write(Payload).Should().BeTrue();
                c.Handle.Disassociate("test", Log);

                // traffic that races our FIN must be drained through TlsHandler, not answered with a reset
                await tls.WriteAsync(new byte[1024]);

                // no close_notify precedes our FIN; SslStream reports the plain EOF as end of stream
                (await ReadToEnd(tls)).Should().Be(4 + Payload.Length);
                await Task.Delay(200);
                SocketError(c.Peer).Should().Be(0);

                c.Peer.Close();
                await c.Channel.CloseCompletion.WaitAsync(TimeSpan.FromSeconds(1));
            }
            finally
            {
                c.Peer.Dispose();
                await c.Transport.Shutdown();
            }
        }

        [Fact(DisplayName = "Should_terminate_well_under_flush_wait_When_an_association_is_open")]
        public async Task Should_terminate_well_under_flush_wait_When_an_association_is_open()
        {
            var config = ConfigurationFactory.ParseString("akka.remote.flush-wait-on-shutdown = 5s").WithFallback(Config);
            var a = ActorSystem.Create("GracefulA", config);
            var b = ActorSystem.Create("GracefulB", config);
            try
            {
                var probe = CreateTestProbe(a);
                var bAddress = ((ExtendedActorSystem)b).Provider.DefaultAddress;
                a.ActorSelection(new RootActorPath(bAddress) / "user" / "missing").Tell(new Identify(1), probe.Ref);
                await probe.ExpectMsgAsync<ActorIdentity>();

                // the peer answers our FIN at once, so no graceful close should sit out the 5 s flush-wait
                var sw = Stopwatch.StartNew();
                await a.Terminate().WaitAsync(TimeSpan.FromSeconds(15));
                sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
            }
            finally
            {
                await ShutdownAsync(a);
                await ShutdownAsync(b);
            }
        }

        [Fact(DisplayName = "Should_find_the_DotNetty_socket_field_the_half_close_depends_on")]
        public void Should_find_the_DotNetty_socket_field_the_half_close_depends_on()
        {
            // a DotNetty upgrade that renames this field silently turns the graceful close back into a reset
            DotNettyTransport.SocketField.Should().NotBeNull();
            DotNettyTransport.SocketField!.FieldType.Should().Be(typeof(Socket));
        }

        [Fact(DisplayName = "Should_force_close_after_flush_wait_When_peer_never_closes")]
        public async Task Should_force_close_after_flush_wait_When_peer_never_closes()
        {
            var sys = ActorSystem.Create("ShortFlushWait", ShortFlushWait);
            try
            {
                var c = await ConnectRawPeer(sys);
                try
                {
                    c.Handle.Disassociate("test", Log);
                    await c.Channel.CloseCompletion.WaitAsync(TimeSpan.FromSeconds(1.5));
                }
                finally
                {
                    c.Peer.Dispose();
                    await c.Transport.Shutdown();
                }
            }
            finally
            {
                await ShutdownAsync(sys);
            }
        }

        [Fact(DisplayName = "Should_refuse_writes_When_disassociated")]
        public async Task Should_refuse_writes_When_disassociated()
        {
            var c = await ConnectRawPeer(Sys);
            try
            {
                c.Handle.Disassociate("test", Log);
                c.Handle.Write(Payload).Should().BeFalse();
            }
            finally
            {
                c.Peer.Dispose();
                await c.Transport.Shutdown();
            }
        }

        [Fact(DisplayName = "Should_finish_transport_shutdown_within_flush_wait_When_a_drain_is_stuck")]
        public async Task Should_finish_transport_shutdown_within_flush_wait_When_a_drain_is_stuck()
        {
            var sys = ActorSystem.Create("ShortFlushWait", ShortFlushWait);
            try
            {
                var c = await ConnectRawPeer(sys);
                try
                {
                    c.Handle.Disassociate("test", Log);

                    var sw = Stopwatch.StartNew();
                    await c.Transport.Shutdown().WaitAsync(TimeSpan.FromSeconds(3));
                    sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1.5));
                    c.Channel.Open.Should().BeFalse();
                }
                finally
                {
                    c.Peer.Dispose();
                    await c.Transport.Shutdown();
                }
            }
            finally
            {
                await ShutdownAsync(sys);
            }
        }

        [Fact(DisplayName = "Should_close_both_sides_without_reset_When_both_disassociate_at_once")]
        public async Task Should_close_both_sides_without_reset_When_both_disassociate_at_once()
        {
            var config = Sys.Settings.Config.GetConfig("akka.remote.dot-netty.tcp");
            var t1 = new TcpTransport(Sys, config);
            var t2 = new TcpTransport(Sys, config);
            try
            {
                var p1 = CreateTestProbe();
                var p2 = CreateTestProbe();
                var (_, l1) = await t1.Listen();
                l1.SetResult(new ActorAssociationEventListener(p1));
                var (a2, l2) = await t2.Listen();
                l2.SetResult(new ActorAssociationEventListener(p2));

                var outbound = await t1.Associate(a2);
                outbound.ReadHandlerSource.SetResult(new ActorHandleEventListener(p1));
                var inbound = (await p2.ExpectMsgAsync<InboundAssociation>()).Association;
                inbound.ReadHandlerSource.SetResult(new ActorHandleEventListener(p2));

                await AwaitConditionAsync(() => Task.FromResult(t1.ConnectionGroup.Count == 2 && t2.ConnectionGroup.Count == 2));
                var chan1 = t1.ConnectionGroup.Single(x => !ReferenceEquals(x, t1.ServerChannel));
                var chan2 = t2.ConnectionGroup.Single(x => !ReferenceEquals(x, t2.ServerChannel));

                await EventFilter.Info(contains: "reset").ExpectAsync(0, async () =>
                {
                    for (var i = 0; i < 10; i++)
                    {
                        outbound.Write(Payload);
                        inbound.Write(Payload);
                    }

                    await Task.WhenAll(
                        Task.Run(() => outbound.Disassociate("test", Log)),
                        Task.Run(() => inbound.Disassociate("test", Log)));

                    // both sides see the other's FIN; neither waits for the 2 s flush-wait deadline
                    await Task.WhenAll(chan1.CloseCompletion, chan2.CloseCompletion).WaitAsync(TimeSpan.FromSeconds(1));
                });
            }
            finally
            {
                await t1.Shutdown();
                await t2.Shutdown();
            }
        }

        private sealed record Connection(TcpTransport Transport, AssociationHandle Handle, IChannel Channel, Socket Peer);

        // A raw socket connects in. No ReadHandlerSource is set, so our side never turns AutoRead on.
        private async Task<Connection> ConnectRawPeer(ActorSystem system, Config? transportConfig = null)
        {
            var transport = new TcpTransport(system, transportConfig ?? system.Settings.Config.GetConfig("akka.remote.dot-netty.tcp"));
            var probe = CreateTestProbe(system);
            var (address, listener) = await transport.Listen();
            listener.SetResult(new ActorAssociationEventListener(probe));

            var peer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await peer.ConnectAsync(IPAddress.Loopback, address.Port!.Value).WaitAsync(TimeSpan.FromSeconds(3));
            var handle = (await probe.ExpectMsgAsync<InboundAssociation>()).Association;
            await AwaitConditionAsync(() => Task.FromResult(transport.ConnectionGroup.Count == 2));
            var channel = transport.ConnectionGroup.Single(x => !ReferenceEquals(x, transport.ServerChannel));
            return new Connection(transport, handle, channel, peer);
        }

        private static async Task<int> ReadToEnd(Stream peer)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var buffer = new byte[4096];
            var total = 0;
            int n;
            while ((n = await peer.ReadAsync(buffer, cts.Token)) > 0)
                total += n;
            return total;
        }

        private static int SocketError(Socket peer) =>
            (int)peer.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error)!;
    }
}
