//-----------------------------------------------------------------------
// <copyright file="ArteryOutboundBurstBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Configuration;
using Akka.Remote;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Remoting.Artery
{
    /// <summary>
    /// End-to-end burst through ONE ordinary outbound lane of a real Artery association: two
    /// in-process actor systems over loopback TCP, one sender, one recipient. Each invocation
    /// tells <see cref="Burst"/> messages and completes when the remote recipient has received
    /// them all, so the measured path covers the lane channel, the outbound stream (and, at
    /// <c>outbound-lanes = 4</c>, the lane MergeHub), TCP, and inbound dispatch.
    /// </summary>
    [Config(typeof(MicroBenchmarkConfig))]
    public class ArteryOutboundBurstBenchmarks
    {
        /// <summary>Messages per invocation; below the default outbound queue size so nothing is dropped.</summary>
        private const int Burst = 2000;

        private const string Message = "burst";

        [Params(1, 4)]
        public int OutboundLanes { get; set; }

        private ActorSystem _sender = null!;
        private ActorSystem _receiver = null!;
        private IActorRef _sink = null!;
        private readonly Countdown _countdown = new();

        [GlobalSetup]
        public void GlobalSetup()
        {
            var config = ConfigurationFactory.ParseString($"""
                akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
                akka.remote.artery.enabled = on
                akka.remote.artery.canonical.hostname = "127.0.0.1"
                akka.remote.artery.canonical.port = 0
                akka.remote.artery.advanced.outbound-lanes = {OutboundLanes}
                """);

            _sender = ActorSystem.Create("burst-sender", config);
            _receiver = ActorSystem.Create("burst-receiver", config);
            _receiver.ActorOf(Props.Create(() => new CountingSink(_countdown)), "sink");

            var port = RARP.For(_receiver).Provider.DefaultAddress.Port;
            _sink = _sender.ActorSelection($"akka://burst-receiver@127.0.0.1:{port}/user/sink")
                .ResolveOne(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();

            SendBurst().GetAwaiter().GetResult();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            _sender.Terminate().Wait(TimeSpan.FromSeconds(15));
            _receiver.Terminate().Wait(TimeSpan.FromSeconds(15));
        }

        [Benchmark(OperationsPerInvoke = Burst)]
        public Task SendBurst()
        {
            var done = _countdown.Arm(Burst);
            for (var i = 0; i < Burst; i++)
                _sink.Tell(Message, ActorRefs.NoSender);
            return done;
        }

        private sealed class Countdown
        {
            private int _remaining;
            private TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task Arm(int count)
            {
                _done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Volatile.Write(ref _remaining, count);
                return _done.Task;
            }

            public void Signal()
            {
                if (Interlocked.Decrement(ref _remaining) == 0)
                    _done.TrySetResult(true);
            }
        }

        private sealed class CountingSink : UntypedActor
        {
            private readonly Countdown _countdown;

            public CountingSink(Countdown countdown) => _countdown = countdown;

            protected override void OnReceive(object message) => _countdown.Signal();
        }
    }
}
