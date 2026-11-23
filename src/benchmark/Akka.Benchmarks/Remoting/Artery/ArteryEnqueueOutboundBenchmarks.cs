//-----------------------------------------------------------------------
// <copyright file="ArteryEnqueueOutboundBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Configuration;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Remoting.Artery
{
    /// <summary>
    /// Allocations per message for an ordinary (non-control, non-large) Tell over a materialized
    /// Artery association, i.e. the <c>EnqueueOutbound</c> send path. A window of messages is sent
    /// to the remote system, which acknowledges each window with a single message.
    /// </summary>
    [Config(typeof(MicroBenchmarkConfig))]
    public class ArteryEnqueueOutboundBenchmarks
    {
        private const int Window = 256;
        private const int Windows = 400;
        private const int MsgCount = Window * Windows;

        private static Config SystemConfig => ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.log-dead-letters = off
            """);

        private sealed class Ack
        {
            public static readonly Ack Instance = new();
        }

        private sealed class Receiver : ReceiveActor
        {
            public Receiver()
            {
                var received = 0;
                ReceiveAny(_ =>
                {
                    if (++received == Window)
                    {
                        received = 0;
                        Sender.Tell(Ack.Instance);
                    }
                });
            }
        }

        private sealed class Start
        {
            public static readonly Start Instance = new();
        }

        private sealed class Sender : ReceiveActor
        {
            public Sender(IActorRef remote, TaskCompletionSource<bool> done)
            {
                var windowsLeft = Windows;
                void SendWindow()
                {
                    for (var i = 0; i < Window; i++)
                        remote.Tell("m", Self);
                }

                Receive<Start>(_ => SendWindow());
                Receive<Ack>(_ =>
                {
                    if (--windowsLeft > 0)
                        SendWindow();
                    else
                        done.TrySetResult(true);
                });
            }
        }

        private ActorSystem _local = null!;
        private ActorSystem _remote = null!;
        private IActorRef _remoteRef = null!;
        private IActorRef _sender = null!;
        private TaskCompletionSource<bool> _done = null!;

        [GlobalSetup]
        public async Task Setup()
        {
            _local = ActorSystem.Create("artery-local", SystemConfig);
            _remote = ActorSystem.Create("artery-remote", SystemConfig);
            _remote.ActorOf(Props.Create(() => new Receiver()), "receiver");

            var address = ((ExtendedActorSystem)_remote).Provider.DefaultAddress;
            _remoteRef = await _local.ActorSelection($"{address}/user/receiver")
                .ResolveOne(TimeSpan.FromSeconds(30));
        }

        [IterationSetup]
        public void IterationSetup()
        {
            _done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _sender = _local.ActorOf(Props.Create(() => new Sender(_remoteRef, _done)));
        }

        [Benchmark(OperationsPerInvoke = MsgCount)]
        public async Task TellToRemote()
        {
            _sender.Tell(Start.Instance);
            await _done.Task;
        }

        [IterationCleanup]
        public void IterationCleanup() => _local.Stop(_sender);

        [GlobalCleanup]
        public void Cleanup()
        {
            _local.Terminate().Wait();
            _remote.Terminate().Wait();
        }
    }
}
