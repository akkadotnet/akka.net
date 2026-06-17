//-----------------------------------------------------------------------
// <copyright file="TcpConcurrentCloseSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
    /// Regression coverage for the <b>concurrent-close</b> read-gate blind spot in
    /// <see cref="TcpTransportConnection"/>.
    /// <para>
    /// The inbound side is consumer-driven (no read pump): the owning <see cref="TcpConnection"/>
    /// actor keeps a <c>ReadAsync</c> in flight on the transport's <c>StreamPipeReader</c>, wrapped
    /// in a <c>ReadGate</c>. The gate serializes <c>Complete()</c> against a read that is still
    /// <em>parked inside</em> the inner stream read (that window is covered by
    /// <see cref="TcpGracefulCloseRaceSpec"/>). But it used to stop tracking the read the moment
    /// <c>_inner.ReadAsync</c> RETURNED — clearing <c>_inflightRead = null</c> in its finally —
    /// while the consumer kept using the returned <c>ReadResult.Buffer</c> OUTSIDE the gate
    /// (<c>new byte[buffer.Length]; buffer.CopyTo(array); reader.AdvanceTo(buffer.End)</c>).
    /// </para>
    /// <para>
    /// Under a concurrent close: <c>CloseAsync → QuiesceAndCompleteAsync</c> sees
    /// <c>_inflightRead == null</c> → calls <c>_inner.Complete()</c> → the StreamPipeReader RECYCLES
    /// the BufferSegments the consumer is mid-<c>CopyTo</c> on → <see cref="ArgumentOutOfRangeException"/>
    /// from <c>BuffersExtensions.CopyTo</c>. That fault was NOT classified as an own-shutdown fault,
    /// so it became an <c>IoTaskFailed</c> → <c>DoCloseConnection(ErrorClosed(...))</c>. The peer only
    /// handles <see cref="Tcp.Closed"/>; <see cref="Tcp.ErrorClosed"/> is a sibling
    /// <see cref="Tcp.ConnectionClosed"/> and <see cref="Tcp.ConnectionClosed"/> implements
    /// <see cref="Akka.Event.IDeadLetterSuppression"/> → the message is dropped SILENTLY → that
    /// connection never reports closed → a coordinating Ask would hang forever. (Reproduced by
    /// <c>TcpOperationsBenchmarks</c> hanging at ClientsCount=10/30; ClientsCount=1 was fine.)
    /// </para>
    /// <para>
    /// The single-exchange close coverage in the other specs never lands inside this post-return
    /// window, so it misses the bug. This spec pins it: open many connections each running a
    /// continuous echo flood (so there is almost always an inbound read that has just RETURNED and
    /// is mid-<c>CopyTo</c> when close lands), then <see cref="Tcp.Close"/> them all at ~the same
    /// instant. Every close must be a clean <see cref="Tcp.Closed"/> on the initiator — never an
    /// <see cref="Tcp.ErrorClosed"/> (how the corruption surfaces) — and all must complete within a
    /// short timeout (no silent hang). It FAILS on the buggy code (ErrorClosed and/or a hung
    /// connection) and PASSES once the gate copies the bytes out internally before clearing
    /// <c>_inflightRead</c>.
    /// </para>
    /// </summary>
    public class TcpConcurrentCloseSpec : AkkaSpec
    {
        // Sized so a read commonly returns a multi-segment buffer (wider CopyTo window) and so the
        // flood keeps inbound data continuously available across all connections.
        private const int PayloadSize = 16 * 1024;

        public TcpConcurrentCloseSpec(ITestOutputHelper output)
            : base("akka.loglevel = INFO\nakka.log-dead-letters = off", output: output)
        {
        }

        /// <summary>
        /// Open N connections, run a continuous echo flood on each, then close them ALL concurrently
        /// while data is in flight. Asserts every initiator reports a clean <see cref="Tcp.Closed"/>
        /// (never <see cref="Tcp.ErrorClosed"/> / <see cref="ArgumentOutOfRangeException"/>) and that
        /// every close completes within a short timeout (no silent hang).
        /// </summary>
        [Fact]
        public async Task Concurrent_Close_under_inbound_flood_should_never_ErrorClose_or_hang()
        {
            const int connectionCount = 30;
            // Several rounds so we get many shots at the narrow post-return CopyTo window. The buggy
            // code corrupts at least one connection within a couple of rounds; the fix is clean on
            // every round.
            const int rounds = 8;

            // Bind a flooding echo server once and reuse across rounds: it echoes everything back so
            // the client side has a steady stream of inbound bytes right up to close time.
            var server = Sys.ActorOf(Props.Create(() => new FloodServer(PayloadSize)), "flood-server");
            var boundProbe = CreateTestProbe("bound");
            server.Tell(new SubscribeBound(boundProbe.Ref));
            var bound = await boundProbe.ExpectMsgAsync<Tcp.Bound>(TimeSpan.FromSeconds(10));
            var endpoint = (IPEndPoint)bound.LocalAddress;

            for (var round = 0; round < rounds; round++)
                await RunRoundAsync(endpoint, connectionCount, round);
        }

        private async Task RunRoundAsync(IPEndPoint endpoint, int connectionCount, int round)
        {
            // Spin up N flood clients. Each reports Closed (or the lack of it) via its own TCS.
            var clients = new List<(IActorRef Actor, TaskCompletionSource<Tcp.ConnectionClosed> Closed)>(connectionCount);
            for (var i = 0; i < connectionCount; i++)
            {
                var closed = new TaskCompletionSource<Tcp.ConnectionClosed>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var actor = Sys.ActorOf(Props.Create(() => new FloodClient(endpoint, PayloadSize, closed)),
                    $"flood-client-{round}-{i}");
                clients.Add((actor, closed));
            }

            // Wait until every client is connected AND has data flowing both ways, so a close will
            // land inside the post-return CopyTo window rather than before any data exists.
            var primed = clients.Select(c => WaitPrimedAsync(c.Actor)).ToArray();
            await Task.WhenAll(primed).WaitAsync(TimeSpan.FromSeconds(30));

            // Let the flood build for a beat so there is reliably an inbound read mid-CopyTo on each
            // connection when the closes land.
            await Task.Delay(40);

            // Close every connection at ~the same instant, mid-flood. The tight loop packs the
            // closes into the narrow post-return window across many connections at once.
            foreach (var c in clients)
                c.Actor.Tell(Close.Instance);

            // Every initiator must observe a clean Closed within a short budget. On the buggy code a
            // corrupted connection either surfaces ErrorClosed or silently drops it (the message is
            // IDeadLetterSuppression) and never reports closed — which this WaitAsync timeout turns
            // into a hard, deterministic failure instead of a hang.
            var results = await Task.WhenAll(clients.Select(c => c.Closed.Task))
                .WaitAsync(TimeSpan.FromSeconds(30));

            for (var i = 0; i < results.Length; i++)
            {
                results[i].Should().BeOfType<Tcp.Closed>(
                    $"connection {i} (round {round}) must close cleanly under concurrent close, was: {Describe(results[i])}");
            }

            // Tear down this round's clients before the next.
            foreach (var c in clients)
                Sys.Stop(c.Actor);
        }

        private static async Task WaitPrimedAsync(IActorRef client)
        {
            // Poll the client's "primed" flag via Ask. Cheap and avoids extra plumbing.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(25);
            while (DateTime.UtcNow < deadline)
            {
                var primed = await client.Ask<bool>(IsPrimed.Instance, TimeSpan.FromSeconds(5));
                if (primed)
                    return;
                await Task.Delay(20);
            }

            throw new TimeoutException("flood client never primed");
        }

        private static string Describe(Tcp.ConnectionClosed closed)
            => closed is Tcp.ErrorClosed err
                ? $"ErrorClosed(\"{err.Cause}\")"
                : closed.GetType().Name;

        private sealed class SubscribeBound
        {
            public SubscribeBound(IActorRef subscriber) => Subscriber = subscriber;
            public IActorRef Subscriber { get; }
        }

        private sealed class IsPrimed
        {
            public static readonly IsPrimed Instance = new();
            private IsPrimed() { }
        }

        private sealed class Close
        {
            public static readonly Close Instance = new();
            private Close() { }
        }

        /// <summary>
        /// Binds and, for each accepted connection, spins up a connection actor that echoes
        /// everything it receives straight back — keeping the client side flooded with inbound data.
        /// </summary>
        private sealed class FloodServer : ReceiveActor
        {
            private IActorRef _subscriber = ActorRefs.Nobody;
            private Tcp.Bound _bound;

            public FloodServer(int payloadSize)
            {
                Context.System.Tcp().Tell(new Tcp.Bind(Self, new IPEndPoint(IPAddress.Loopback, 0)));

                Receive<SubscribeBound>(sub =>
                {
                    _subscriber = sub.Subscriber;
                    if (_bound != null)
                        _subscriber.Tell(_bound);
                });
                Receive<Tcp.Bound>(b =>
                {
                    _bound = b;
                    if (!_subscriber.IsNobody())
                        _subscriber.Tell(b);
                });
                Receive<Tcp.Connected>(_ =>
                {
                    var conn = Context.ActorOf(Props.Create(() => new FloodServerConnection(Sender)));
                    Sender.Tell(new Tcp.Register(conn));
                });
            }
        }

        private sealed class FloodServerConnection : ReceiveActor
        {
            public FloodServerConnection(IActorRef connection)
            {
                // Pure echo: bounce every received byte back. Combined with the client's continuous
                // re-send this keeps inbound data flowing on BOTH sides at all times.
                Receive<Tcp.Received>(r => connection.Tell(Tcp.Write.Create(r.Data)));
                Receive<Tcp.ConnectionClosed>(_ => Context.Stop(Self));
            }
        }

        /// <summary>
        /// Connects, fires an opening burst, and keeps re-sending on every inbound chunk so there is
        /// a continuous inbound flood. Reports the first <see cref="Tcp.ConnectionClosed"/> it sees
        /// through the supplied TCS. Answers <see cref="IsPrimed"/> once data is flowing, and issues
        /// <see cref="Tcp.Close"/> when told to <see cref="Close"/>.
        /// </summary>
        private sealed class FloodClient : ReceiveActor
        {
            private const int OpeningBurst = 8;

            private readonly Tcp.WriteCommand _write;
            private readonly TaskCompletionSource<Tcp.ConnectionClosed> _closed;
            private IActorRef _connection = ActorRefs.Nobody;
            private bool _primed;
            private bool _closing;

            public FloodClient(IPEndPoint endpoint, int payloadSize, TaskCompletionSource<Tcp.ConnectionClosed> closed)
            {
                _closed = closed;
                var payload = new byte[payloadSize];
                for (var i = 0; i < payload.Length; i++)
                    payload[i] = (byte)(i & 0xFF);
                _write = Tcp.Write.Create(payload.AsMemory());

                Context.System.Tcp().Tell(new Tcp.Connect(endpoint, timeout: TimeSpan.FromSeconds(5)));

                Receive<Tcp.Connected>(_ =>
                {
                    _connection = Sender;
                    Sender.Tell(new Tcp.Register(Self));
                    for (var i = 0; i < OpeningBurst; i++)
                        _connection.Tell(_write);
                });

                Receive<Tcp.Received>(_ =>
                {
                    _primed = true;
                    if (!_closing)
                        _connection.Tell(_write); // keep the flood going
                });

                Receive<IsPrimed>(_ => Sender.Tell(_primed && !_connection.IsNobody()));

                Receive<Close>(_ =>
                {
                    _closing = true;
                    if (!_connection.IsNobody())
                        _connection.Tell(Tcp.Close.Instance);
                });

                Receive<Tcp.ConnectionClosed>(c => _closed.TrySetResult(c));
                Receive<Tcp.CommandFailed>(_ => { /* ignore write failures during teardown */ });
            }
        }
    }
}
