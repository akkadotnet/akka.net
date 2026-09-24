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
using System.Threading;
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

        /// <summary>
        /// Creates a loopback listener. When <paramref name="receiveBufferSize"/> is given, it is
        /// applied to the LISTENING socket before <c>Listen</c> -- the TCP window-scale factor is
        /// fixed at the SYN/SYN-ACK handshake, so setting <see cref="Socket.ReceiveBufferSize"/> on
        /// the socket returned by <c>Accept</c> is too late to shrink the effective window on every
        /// platform (notably Windows, where SO_SNDBUF on the sender is only advisory). Setting it on
        /// the listener beforehand is what actually caps the window an accepted connection can grow
        /// to, cross-platform.
        /// </summary>
        private static (Socket Listener, IPEndPoint Endpoint) CreateListener(int? receiveBufferSize = null)
        {
            var listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            if (receiveBufferSize is { } size)
                listener.ReceiveBufferSize = size;
            listener.Listen(1);
            return (listener, (IPEndPoint)listener.LocalEndPoint!);
        }

        [Fact(DisplayName =
            "Should_NotReport_ConfirmedClosed_Before_OutputDrains_When_PeerFINsEarly")]
        public async Task Should_not_report_ConfirmedClosed_before_output_drains_when_peer_FINs_early()
        {
            // Shrink the window on the LISTENER (before Listen) so the effective receive window
            // is genuinely small, cross-platform -- see CreateListener's remarks. SO_SNDBUF on
            // Windows is only advisory, so we also raise the payload well past what any
            // reasonable kernel buffer combination could silently absorb.
            var (listener, endpoint) = CreateListener(receiveBufferSize: 16384);
            using var _ = listener;

            var connectCommander = CreateTestProbe();
            connectCommander.Send(Sys.Tcp(), new Tcp.Connect(endpoint,
                options: new Inet.SocketOption[] { new Inet.SO.SendBufferSize(16384) }));

            using var peer = await listener.AcceptAsync();

            await connectCommander.ExpectMsgAsync<Tcp.Connected>();
            var connection = connectCommander.LastSender;

            var handler = CreateTestProbe();
            connectCommander.Send(connection, new Tcp.Register(handler.Ref));

            // Several MB against a 16 KB send/receive window, written while the peer never
            // reads: the drain triggered by ConfirmedClose has real, unsent bytes to push once
            // the peer's FIN lands, and the kernel cannot absorb it all regardless of platform.
            const int totalBytes = 4 * 1024 * 1024;
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
            // the rest of the payload through. Bounded so a regression that reintroduces the
            // hang fails the test instead of the test run itself hanging.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var received = new byte[totalBytes];
            var readTotal = 0;
            while (readTotal < totalBytes)
            {
                var n = await peer.ReceiveAsync(received.AsMemory(readTotal, totalBytes - readTotal),
                    SocketFlags.None, cts.Token);
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

            await peerReadTask.WaitAsync(TimeSpan.FromSeconds(10));
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
            await peerTask.WaitAsync(TimeSpan.FromSeconds(10));

            received.ToArray().Should().Equal(payload);
        }

        [Fact(DisplayName =
            "Should_GracefullyCloseSocket_So_Peer_Gets_Every_Byte_Then_A_Clean_EOF_After_ConfirmedClosed")]
        public async Task Should_gracefully_close_socket_so_peer_gets_every_byte_then_a_clean_EOF_after_ConfirmedClosed()
        {
            // Same shrunk-window setup as the early-FIN test above, but smaller (this test
            // doesn't need "several MB", just enough that a single ConfirmedClose drains across
            // more than one socket-buffer's worth of data) and it asserts something different:
            // not WHEN ConfirmedClosed arrives, but what state the raw socket is left in once it
            // genuinely does.
            var (listener, endpoint) = CreateListener(receiveBufferSize: 8192);
            using var _ = listener;

            var connectCommander = CreateTestProbe();
            connectCommander.Send(Sys.Tcp(), new Tcp.Connect(endpoint,
                options: new Inet.SocketOption[] { new Inet.SO.SendBufferSize(8192) }));

            using var peer = await listener.AcceptAsync();

            await connectCommander.ExpectMsgAsync<Tcp.Connected>();
            var connection = connectCommander.LastSender;

            var handler = CreateTestProbe();
            connectCommander.Send(connection, new Tcp.Register(handler.Ref));

            const int totalBytes = 160 * 1024;
            var payload = new byte[totalBytes];
            new Random(86291978).NextBytes(payload);

            const int chunk = 32 * 1024;
            for (var offset = 0; offset < totalBytes; offset += chunk)
            {
                var len = Math.Min(chunk, totalBytes - offset);
                handler.Send(connection, Tcp.Write.Create(payload.AsMemory(offset, len)));
            }

            handler.Send(connection, Tcp.ConfirmedClose.Instance);

            // The peer FINs without ever reading, same race as the early-FIN test above. Once
            // the peer starts reading below, the drain can finish and ConfirmedClosed genuinely
            // arrives. What THIS test checks is what happens to the raw socket once that
            // happens: without the PostStop fix, PostStop unconditionally Aborts (linger 0,
            // RST) even a fully-drained ConfirmedClose, which can in principle make the peer's
            // OS discard bytes still sitting in its own kernel receive buffer, or interrupt a
            // pending read at the very tail of the transfer with a ConnectionReset instead of a
            // clean EOF -- this is what a code reviewer's manual testing observed (12,288 of
            // 163,840 bytes, then ConnectionReset). On this repo's Linux CI/dev environment the
            // race window did not reproduce it deterministically across repeated local runs
            // (Linux appears not to re-arm the linger-0 RST once shutdown(SHUT_WR) already ran),
            // but the fix is still correct: PostStop should not force an abortive close on a
            // connection it already knows finished draining and closing gracefully. This test
            // is therefore a regression guard for the fixed behavior (full transfer + a clean,
            // non-exceptional EOF), not a proven fail-on-revert repro on every platform.
            peer.Shutdown(SocketShutdown.Send);

            // Stall before reading anything, same as the early-FIN test above: this lets the
            // write pump get meaningfully ahead of the peer (blocked on the shrunk window,
            // since nothing is draining it yet) before the peer starts consuming, so there is
            // real sent-but-unread data sitting in the peer's kernel buffer for the eventual
            // close to either preserve (fixed) or let an RST discard (unfixed).
            await handler.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(750));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var received = new byte[totalBytes];
            var readTotal = 0;
            while (readTotal < totalBytes)
            {
                var n = await peer.ReceiveAsync(received.AsMemory(readTotal, totalBytes - readTotal),
                    SocketFlags.None, cts.Token);
                if (n == 0)
                    break;
                readTotal += n;
            }

            readTotal.Should().Be(totalBytes);
            received.Should().Equal(payload);

            await handler.ExpectMsgAsync<Tcp.ConfirmedClosed>(TimeSpan.FromSeconds(10));

            // A gracefully-closed socket ends in a FIN: one more read must return a clean,
            // zero-byte EOF, not throw ConnectionReset.
            var eofBuffer = new byte[1];
            var eofRead = await peer.ReceiveAsync(eofBuffer.AsMemory(), SocketFlags.None, cts.Token);
            eofRead.Should().Be(0);
        }

        [Fact(DisplayName =
            "Should_Complete_ConfirmedClose_With_ErrorClosed_When_ReadPump_Fails_While_Reading_Suspended")]
        public async Task Should_complete_ConfirmedClose_with_ErrorClosed_when_read_pump_fails_while_reading_suspended()
        {
            var (listener, endpoint) = CreateListener();
            using var _ = listener;

            var connectCommander = CreateTestProbe();
            // Pull mode: reading stays suspended until an explicit ResumeReading, which this
            // test never sends. That is what makes the read side's failure surface ONLY as
            // ReadPumpFailed (the transport-level background read pump observed a fault)
            // rather than through the normal PipeReadCompleted/HandlePipeRead path, which
            // already handles an I/O error directly via HandleIoError regardless of this fix.
            connectCommander.Send(Sys.Tcp(), new Tcp.Connect(endpoint, pullMode: true));

            using var peer = await listener.AcceptAsync();

            await connectCommander.ExpectMsgAsync<Tcp.Connected>();
            var connection = connectCommander.LastSender;

            var handler = CreateTestProbe();
            connectCommander.Send(connection, new Tcp.Register(handler.Ref));

            var payload = Encoding.UTF8.GetBytes("small write, drains before the reset");

            // Synchronize on the exact point _outputShutdown becomes true for this
            // ConfirmedClose, so the peer's reset below is guaranteed to land AFTER our own
            // drain has already finished -- this test is specifically about the "waiting on a
            // _peerClosed that will never come" hang, not the drain-completion race the other
            // specs in this file cover.
            await EventFilter.Debug(contains: "FIN sent, waiting for peer FIN").ExpectOneAsync(() =>
            {
                handler.Send(connection, Tcp.Write.Create(payload.AsMemory()));
                handler.Send(connection, Tcp.ConfirmedClose.Instance);
                return Task.CompletedTask;
            });

            // An abrupt RST, not a clean FIN: linger(true, 0) forces the OS to reset the
            // connection on close instead of running the normal FIN/ACK sequence.
            peer.LingerState = new LingerOption(true, 0);
            peer.Close();

            // Without the fix in ClosingBehaviour's ReadPumpFailed handler, this hangs
            // forever: reading is suspended (pull mode), so no PipeReadCompleted/StreamEof is
            // ever in flight to notice the reset any other way, and TryFinishClose's
            // ConfirmedClosed branch waits on a _peerClosed that will never be set.
            await handler.ExpectMsgAsync<Tcp.ErrorClosed>(TimeSpan.FromSeconds(10));
        }

        [Fact(DisplayName =
            "Should_CompleteWithClosed_When_Close_Follows_ConfirmedClose_And_Peer_Never_FINs")]
        public async Task Should_complete_with_Closed_when_Close_follows_ConfirmedClose_and_peer_never_FINs()
        {
            // Regression test for #8634: once ClosingBehaviour entered a ConfirmedClose, a
            // later Tcp.Close was silently dropped, so the connection waited forever for a
            // peer FIN that may never come (what Artery's half-closed inbound connections hit
            // on shutdown). Here the peer stays fully open and never sends one.
            var (listener, endpoint) = CreateListener();
            using var _ = listener;

            var connectCommander = CreateTestProbe();
            connectCommander.Send(Sys.Tcp(), new Tcp.Connect(endpoint));

            using var peer = await listener.AcceptAsync();

            await connectCommander.ExpectMsgAsync<Tcp.Connected>();
            var connection = connectCommander.LastSender;

            var handler = CreateTestProbe();
            connectCommander.Send(connection, new Tcp.Register(handler.Ref));

            var payload = Encoding.UTF8.GetBytes("bytes written before the ConfirmedClose, must all arrive");

            // Wait until our own FIN is actually sent (_outputShutdown) before asking for a
            // full Close -- this exercises the "already drained" branch of the fix, where
            // Close must finish the connection right away.
            await EventFilter.Debug(contains: "FIN sent, waiting for peer FIN").ExpectOneAsync(() =>
            {
                handler.Send(connection, Tcp.Write.Create(payload.AsMemory()));
                handler.Send(connection, Tcp.ConfirmedClose.Instance);
                return Task.CompletedTask;
            });

            handler.Send(connection, Tcp.Close.Instance);

            await handler.ExpectMsgAsync<Tcp.Closed>(TimeSpan.FromSeconds(2));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var received = new byte[payload.Length];
            var readTotal = 0;
            while (readTotal < payload.Length)
            {
                var n = await peer.ReceiveAsync(received.AsMemory(readTotal, payload.Length - readTotal),
                    SocketFlags.None, cts.Token);
                if (n == 0)
                    break;
                readTotal += n;
            }

            readTotal.Should().Be(payload.Length);
            received.Should().Equal(payload);

            // A gracefully-closed socket ends in a FIN, not a reset.
            var eofBuffer = new byte[1];
            var eofRead = await peer.ReceiveAsync(eofBuffer.AsMemory(), SocketFlags.None, cts.Token);
            eofRead.Should().Be(0);
        }

        [Fact(DisplayName =
            "Should_WaitForOutputDrain_Before_Closed_When_Close_Follows_ConfirmedClose_WhileDraining")]
        public async Task Should_wait_for_output_drain_before_Closed_when_Close_follows_ConfirmedClose_while_draining()
        {
            // Regression test for #8634, the other half: Close arrives WHILE the write side is
            // still draining (peer isn't reading yet) -- it must not finish early, only once
            // our own output has actually drained, peer FIN or not.
            var (listener, endpoint) = CreateListener(receiveBufferSize: 16384);
            using var _ = listener;

            var connectCommander = CreateTestProbe();
            connectCommander.Send(Sys.Tcp(), new Tcp.Connect(endpoint,
                options: new Inet.SocketOption[] { new Inet.SO.SendBufferSize(16384) }));

            using var peer = await listener.AcceptAsync();

            await connectCommander.ExpectMsgAsync<Tcp.Connected>();
            var connection = connectCommander.LastSender;

            var handler = CreateTestProbe();
            connectCommander.Send(connection, new Tcp.Register(handler.Ref));

            const int totalBytes = 4 * 1024 * 1024;
            var payload = new byte[totalBytes];
            new Random(86291979).NextBytes(payload);

            const int chunk = 64 * 1024;
            for (var offset = 0; offset < totalBytes; offset += chunk)
            {
                var len = Math.Min(chunk, totalBytes - offset);
                handler.Send(connection, Tcp.Write.Create(payload.AsMemory(offset, len)));
            }

            handler.Send(connection, Tcp.ConfirmedClose.Instance);
            handler.Send(connection, Tcp.Close.Instance);

            // Must NOT complete yet -- our output is still draining and the peer isn't reading.
            await handler.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(750));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var received = new byte[totalBytes];
            var readTotal = 0;
            while (readTotal < totalBytes)
            {
                var n = await peer.ReceiveAsync(received.AsMemory(readTotal, totalBytes - readTotal),
                    SocketFlags.None, cts.Token);
                if (n == 0)
                    break;
                readTotal += n;
            }

            readTotal.Should().Be(totalBytes);
            received.Should().Equal(payload);

            await handler.ExpectMsgAsync<Tcp.Closed>(TimeSpan.FromSeconds(10));

            var eofBuffer = new byte[1];
            var eofRead = await peer.ReceiveAsync(eofBuffer.AsMemory(), SocketFlags.None, cts.Token);
            eofRead.Should().Be(0);
        }
    }
}
