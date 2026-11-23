//-----------------------------------------------------------------------
// <copyright file="ArteryInboundDispatchBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Reflection;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote;
using Akka.Remote.Artery;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Remoting.Artery
{
    /// <summary>
    /// Cost of the inbound Artery dispatch step for one already-decoded ordinary message on the
    /// success path (all paths valid, nothing throws). One operation is one call of the real
    /// <c>ArteryRemoting.DispatchInbound</c> (the lanes = 1 sink callback) on an envelope carrying a
    /// decoded frame with literal sender/recipient paths and no ref ids: control/system-message checks,
    /// recipient resolve, sender resolve, and <c>Tell</c> into the recipient. No sockets, framing or
    /// deserialization are involved. For a local mailbox recipient an operation ends once the
    /// message is enqueued; the recipient actor drains it asynchronously, and the iteration cleanup
    /// waits for the drain so mailboxes stay bounded.
    /// </summary>
    [MemoryDiagnoser]
    public class ArteryInboundDispatchBenchmarks
    {
        private const int Batch = 1000;

        private sealed class Sink : UntypedActor
        {
            public static long Received;
            protected override void OnReceive(object message) => Interlocked.Increment(ref Received);
        }

        /// <summary>Stands in for an Ask future: a registered temp-path ref that counts deliveries.</summary>
        private sealed class CountingTempRef : MinimalActorRef
        {
            private readonly IActorRefProvider _provider;
            public long Received;
            public CountingTempRef(IActorRefProvider provider, ActorPath path) { _provider = provider; Path = path; }
            public override ActorPath Path { get; }
            public override IActorRefProvider Provider => _provider;
            protected override void TellInternal(object message, IActorRef sender) => Received++;
        }

        private ActorSystem _system = null!;
        private Action<IInboundEnvelope, ArteryRemoteSenderCache> _dispatch = null!;
        private readonly ArteryRemoteSenderCache _senders = new();
        private IInboundEnvelope _tellWithSender = null!;
        private IInboundEnvelope _tellNoSender = null!;
        private IInboundEnvelope _askReply = null!;
        private CountingTempRef _temp = null!;
        private long _sent;

        [GlobalSetup]
        public void Setup()
        {
            _system = ActorSystem.Create("inbound-dispatch", ConfigurationFactory.ParseString("""
                akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
                akka.remote.artery.enabled = on
                akka.remote.artery.canonical.hostname = "127.0.0.1"
                akka.remote.artery.canonical.port = 0
                akka.log-dead-letters = off
                akka.loglevel = ERROR
                """));
            var ext = (ExtendedActorSystem)_system;
            var provider = (RemoteActorRefProvider)ext.Provider;
            var transport = provider.Transport;

            var method = transport.GetType().GetMethod("DispatchInbound", BindingFlags.Instance | BindingFlags.NonPublic)
                         ?? throw new InvalidOperationException("DispatchInbound not found");
            _dispatch = (Action<IInboundEnvelope, ArteryRemoteSenderCache>)Delegate.CreateDelegate(
                typeof(Action<IInboundEnvelope, ArteryRemoteSenderCache>), transport, method);

            var sink = _system.ActorOf(Props.Create(() => new Sink()), "sink");
            _temp = new CountingTempRef(provider, provider.TempPath());
            provider.RegisterTempActor(_temp, _temp.Path);

            string Ser(IActorRef r) => Akka.Serialization.Serialization.WithTransport(ext, () => Akka.Serialization.Serialization.SerializedActorPath(r));
            var recipientPath = Ser(sink);
            var tempPath = Ser(_temp);
            var senderPath = "akka://remote-peer@10.1.2.3:4053/user/sender#1234567";

            const string msg = "ping";
            IInboundEnvelope Envelope(string? sender, string recipient)
            {
                using var frame = Akka.Remote.Artery.ArteryEnvelopeCodec.Encode(_system.Serialization, 42L, sender, recipient, msg);
                var decoded = Akka.Remote.Artery.ArteryEnvelopeCodec.Decode(new ReadOnlySequence<byte>(frame.WrittenMemory.Slice(4).ToArray()));
                return new InboundEnvelope(msg, null, null, 42L, 1, "S") { Frame = decoded };
            }

            _tellWithSender = Envelope(senderPath, recipientPath);
            _tellNoSender = Envelope(null, recipientPath);
            _askReply = Envelope(senderPath, tempPath);

            // Warm the path caches and the actor so first-use costs are outside the measurement.
            for (var i = 0; i < 100; i++)
            {
                _dispatch(_tellWithSender, _senders);
                _dispatch(_askReply, _senders);
            }
            _sent = 100;
            IterationCleanup();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            if (!_system.Terminate().Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The actor system did not terminate within 10 seconds.");
        }

        [IterationSetup]
        public void IterationSetup()
        {
            Sink.Received = 0;
            _sent = 0;
        }

        [IterationCleanup]
        public void IterationCleanup()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (Volatile.Read(ref Sink.Received) < _sent && sw.Elapsed < TimeSpan.FromSeconds(30))
                Thread.Sleep(1);

            if (Volatile.Read(ref Sink.Received) < _sent)
                throw new TimeoutException($"Only {Volatile.Read(ref Sink.Received)} of {_sent} dispatched messages were delivered within 30 seconds.");
        }

        /// <summary>Local user actor recipient, remote sender (typical Tell with sender). Per op: one DispatchInbound.</summary>
        [Benchmark(OperationsPerInvoke = Batch)]
        public void LocalRecipient_RemoteSender()
        {
            var d = _dispatch; var e = _tellWithSender;
            for (var i = 0; i < Batch; i++) d(e, _senders);
            _sent += Batch;
        }

        /// <summary>Local user actor recipient, no sender. Per op: one DispatchInbound.</summary>
        [Benchmark(OperationsPerInvoke = Batch)]
        public void LocalRecipient_NoSender()
        {
            var d = _dispatch; var e = _tellNoSender;
            for (var i = 0; i < Batch; i++) d(e, _senders);
            _sent += Batch;
        }

        /// <summary>Temp (Ask future) recipient, remote sender. Per op: one DispatchInbound; delivery is synchronous.</summary>
        [Benchmark(OperationsPerInvoke = Batch)]
        public void TempRecipient_RemoteSender()
        {
            var d = _dispatch; var e = _askReply;
            for (var i = 0; i < Batch; i++) d(e, _senders);
        }
    }
}
