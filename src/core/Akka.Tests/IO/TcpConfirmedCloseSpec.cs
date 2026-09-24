//-----------------------------------------------------------------------
// <copyright file="TcpConfirmedCloseSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.IO
{
    /// <summary>
    /// Regression coverage for https://github.com/akkadotnet/akka.net/issues/8629.
    ///
    /// <see cref="Tcp.ConfirmedClose"/> is a half-close: flush pending writes, send our own FIN,
    /// then wait for the peer's FIN before reporting <see cref="Tcp.ConfirmedClosed"/>. Two bugs
    /// existed in that sequence, both in <c>TcpConnection.ClosingBehaviour</c>:
    /// <list type="number">
    /// <item><description>
    /// The peer's FIN (<c>StreamEof</c>) completed a <see cref="Tcp.ConfirmedClose"/> immediately,
    /// without checking whether OUR OWN output had actually finished draining. If the peer's FIN
    /// landed while the write side was still flushing, the connection reported
    /// <see cref="Tcp.ConfirmedClosed"/> early, the actor stopped, and <c>PostStop</c>'s abort
    /// (linger 0, RST) discarded whatever was still unsent.
    /// </description></item>
    /// <item><description>
    /// With <c>keepOpenOnPeerClosed</c>, a peer FIN that arrives BEFORE <see cref="Tcp.ConfirmedClose"/>
    /// is requested sets the "peer closed" flag once, via <c>PeerSentEofBehaviour</c>. The
    /// <c>StreamEof</c> that would normally finish a later <see cref="Tcp.ConfirmedClose"/> never
    /// arrives again, and the completion check used to unconditionally wait for it - so the close
    /// never finished.
    /// </description></item>
    /// </list>
    /// Each connection here has an Akka-managed side (via <see cref="Sys"/>'s TCP extension) and a
    /// raw, unmanaged <see cref="Socket"/> peer that the test drives directly, so the test controls
    /// exactly when the peer reads and when its FIN is sent - the two things the race depends on.
    /// </summary>
    public class TcpConfirmedCloseSpec : AkkaSpec
    {
        public TcpConfirmedCloseSpec(ITestOutputHelper output)
            : base(@"akka.loglevel = DEBUG
                     akka.io.tcp.trace-logging = true", output: output)
        {
        }

        private static (Socket Listener, IPEndPoint Endpoint) CreateListener()
        {
            var listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            return (listener, (IPEndPoint)listener.LocalEndPoint!);
        }

        [Fact(DisplayName =
            "Should_NotReport_ConfirmedClosed_Before_OutputDrains_When_PeerFINsEarly")]
        public async Task Should_not_report_ConfirmedClosed_before_output_drains_when_peer_FINs_early()
        {
            var (listener, endpoint) = CreateListener();
            using var _ = listener;

            // Shrink both sides of the pipe so a few MB of unread data is guaranteed to fill the
            // kernel send/receive buffers quickly and deterministically, without depending on
            // platform-specific default buffer sizes.
            var connectCommander = CreateTestProbe();
            connectCommander.Send(Sys.Tcp(), new Tcp.Connect(endpoint,
                options: new Inet.SocketOption[] { new Inet.SO.SendBufferSize(16384) }));

            using var peer = await listener.AcceptAsync();
            peer.ReceiveBufferSize = 16384;

            await connectCommander.ExpectMsgAsync<Tcp.Connected>();
            var connection = connectCommander.LastSender;

            var handler = CreateTestProbe();
            connectCommander.Send(connection, new Tcp.Register(handler.Ref));

            // Several hundred KB against a 16 KB send/receive window, written while the peer
            // never reads: the drain triggered by ConfirmedClose has real, unsent bytes to push
            // once the peer's FIN lands.
            const int totalBytes = 768 * 1024;
            var payload = new byte[totalBytes];
            new Random(20260924).NextBytes(payload);

            const int chunk = 64 * 1024;
            for (var offset = 0; offset < totalBytes; offset += chunk)
            {
                var len = Math.Min(chunk, totalBytes - offset);
                handler.Send(connection, Tcp.Write.Create(payload.AsMemory(offset, len)));
            }

            handler.Send(connection, Tcp.ConfirmedClose.Instance);

            // The peer FINs its own send side WITHOUT ever reading -- this races the peer's FIN
            // against our still-draining output, which is exactly what #8629 got wrong.
            peer.Shutdown(SocketShutdown.Send);

            // Must NOT complete yet -- our output is still draining and the peer isn't reading.
            await handler.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(750));

            // Now let the peer actually drain the socket so the write pump can finish pushing
            // the rest of the payload through.
            var received = new byte[totalBytes];
            var readTotal = 0;
            while (readTotal < totalBytes)
            {
                var n = await peer.ReceiveAsync(received.AsMemory(readTotal, totalBytes - readTotal),
                    SocketFlags.None);
                if (n == 0)
                    break;
                readTotal += n;
            }

            readTotal.Should().Be(totalBytes);
            received.Should().Equal(payload);

            await handler.ExpectMsgAsync<Tcp.ConfirmedClosed>(TimeSpan.FromSeconds(10));
        }

        [Fact(DisplayName =
            "Should_Complete_ConfirmedClose_When_PeerFIN_AlreadyArrived_Via_KeepOpenOnPeerClosed")]
        public async Task Should_complete_ConfirmedClose_when_peer_FIN_already_arrived_via_KeepOpenOnPeerClosed()
        {
            var (listener, endpoint) = CreateListener();
            using var _ = listener;

            var connectCommander = CreateTestProbe();
            connectCommander.Send(Sys.Tcp(), new Tcp.Connect(endpoint));

            using var peer = await listener.AcceptAsync();

            await connectCommander.ExpectMsgAsync<Tcp.Connected>();
            var connection = connectCommander.LastSender;

            var handler = CreateTestProbe();
            connectCommander.Send(connection, new Tcp.Register(handler.Ref, keepOpenOnPeerClosed: true));

            // The peer FINs FIRST, well before ConfirmedClose is ever requested.
            peer.Shutdown(SocketShutdown.Send);
            await handler.ExpectMsgAsync<Tcp.PeerClosed>(TimeSpan.FromSeconds(5));

            // The peer keeps reading in the background so our write can actually drain.
            var received = new MemoryStream();
            var peerReadTask = Task.Run(async () =>
            {
                var buffer = new byte[4096];
                while (true)
                {
                    int n;
                    try
                    {
                        n = await peer.ReceiveAsync(buffer.AsMemory(), SocketFlags.None);
                    }
                    catch (SocketException)
                    {
                        break;
                    }

                    if (n == 0)
                        break;
                    received.Write(buffer, 0, n);
                }
            });

            var payload = Encoding.UTF8.GetBytes("still have data to send after the peer FIN'd");
            handler.Send(connection, Tcp.Write.Create(payload.AsMemory()));
            handler.Send(connection, Tcp.ConfirmedClose.Instance);

            // Without the fix this hangs forever: the StreamEof that set the peer-closed flag
            // already happened before ConfirmedClose was even requested, so the close never
            // re-checks it.
            await handler.ExpectMsgAsync<Tcp.ConfirmedClosed>(TimeSpan.FromSeconds(10));

            await peerReadTask;
            received.ToArray().Should().Equal(payload);
        }

        [Fact(DisplayName = "Should_Complete_ConfirmedClose_Normally_When_There_Is_No_Race_With_PeerFIN")]
        public async Task Should_complete_ConfirmedClose_normally_when_there_is_no_race_with_peer_FIN()
        {
            var (listener, endpoint) = CreateListener();
            using var _ = listener;

            var connectCommander = CreateTestProbe();
            connectCommander.Send(Sys.Tcp(), new Tcp.Connect(endpoint));

            using var peer = await listener.AcceptAsync();

            await connectCommander.ExpectMsgAsync<Tcp.Connected>();
            var connection = connectCommander.LastSender;

            var handler = CreateTestProbe();
            connectCommander.Send(connection, new Tcp.Register(handler.Ref));

            var payload = Encoding.UTF8.GetBytes("no race here, this should just work");

            // A well-behaved peer: read everything, then FIN back once it sees our own FIN.
            var received = new MemoryStream();
            var peerTask = Task.Run(async () =>
            {
                var buffer = new byte[4096];
                while (true)
                {
                    var n = await peer.ReceiveAsync(buffer.AsMemory(), SocketFlags.None);
                    if (n == 0)
                        break;
                    received.Write(buffer, 0, n);
                }

                peer.Shutdown(SocketShutdown.Send);
            });

            handler.Send(connection, Tcp.Write.Create(payload.AsMemory()));
            handler.Send(connection, Tcp.ConfirmedClose.Instance);

            await handler.ExpectMsgAsync<Tcp.ConfirmedClosed>(TimeSpan.FromSeconds(10));
            await peerTask;

            received.ToArray().Should().Equal(payload);
        }
    }
}
