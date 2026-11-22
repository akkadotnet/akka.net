//-----------------------------------------------------------------------
// <copyright file="ArteryInboundDispatchBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
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
    /// <c>ArteryRemoting.DispatchInbound(IInboundEnvelope)</c> (the lanes = 1 sink callback) on an
    /// envelope carrying literal sender/recipient path strings: control/system-message checks,
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
        private Action<IInboundEnvelope> _dispatch = null!;
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
            _dispatch = (Action<IInboundEnvelope>)Delegate.CreateDelegate(typeof(Action<IInboundEnvelope>), transport, method);

            var sink = _system.ActorOf(Props.Create(() => new Sink()), "sink");
            _temp = new CountingTempRef(provider, provider.TempPath());
            provider.RegisterTempActor(_temp, _temp.Path);

            string Ser(IActorRef r) => Akka.Serialization.Serialization.WithTransport(ext, () => Akka.Serialization.Serialization.SerializedActorPath(r));
            var recipientPath = Ser(sink);
            var tempPath = Ser(_temp);
            var senderPath = "akka://remote-peer@10.1.2.3:4053/user/sender#1234567";

            const string msg = "ping";
            _tellWithSender = new InboundEnvelope(msg, senderPath, recipientPath, 42L, 1, "S");
            _tellNoSender = new InboundEnvelope(msg, null, recipientPath, 42L, 1, "S");
            _askReply = new InboundEnvelope(msg, senderPath, tempPath, 42L, 1, "S");

            // Warm the path caches and the actor so first-use costs are outside the measurement.
            for (var i = 0; i < 100; i++)
            {
                _dispatch(_tellWithSender);
                _dispatch(_askReply);
            }
            _sent = 100;
            IterationCleanup();
        }

        [GlobalCleanup]
        public void Cleanup() => _system.Terminate().Wait(TimeSpan.FromSeconds(10));

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
        }

        /// <summary>Local user actor recipient, remote sender (typical Tell with sender). Per op: one DispatchInbound.</summary>
        [Benchmark(OperationsPerInvoke = Batch)]
        public void LocalRecipient_RemoteSender()
        {
            var d = _dispatch; var e = _tellWithSender;
            for (var i = 0; i < Batch; i++) d(e);
            _sent += Batch;
        }

        /// <summary>Local user actor recipient, no sender. Per op: one DispatchInbound.</summary>
        [Benchmark(OperationsPerInvoke = Batch)]
        public void LocalRecipient_NoSender()
        {
            var d = _dispatch; var e = _tellNoSender;
            for (var i = 0; i < Batch; i++) d(e);
            _sent += Batch;
        }

        /// <summary>Temp (Ask future) recipient, remote sender. Per op: one DispatchInbound; delivery is synchronous.</summary>
        [Benchmark(OperationsPerInvoke = Batch)]
        public void TempRecipient_RemoteSender()
        {
            var d = _dispatch; var e = _askReply;
            for (var i = 0; i < Batch; i++) d(e);
        }
    }
}
