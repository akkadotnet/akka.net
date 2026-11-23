//-----------------------------------------------------------------------
// <copyright file="ArteryNumericRefAskBenchmarks.cs" company="Akka.NET Project">
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
using Akka.Remote;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Remoting.Artery
{
    /// <summary>
    /// Ask round trip between two in-process ActorSystems over Artery TCP loopback, with
    /// <c>akka.remote.artery.advanced.numeric-refs</c> off and on.
    /// </summary>
    [Config(typeof(MicroBenchmarkConfig))]
    public class ArteryNumericRefAskBenchmarks
    {
        private const int Concurrent = 64;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private sealed class Echo : ReceiveActor
        {
            public Echo() => ReceiveAny(message => Sender.Tell(message));
        }

        [Params("off", "on")]
        public string NumericRefs { get; set; } = "off";

        private ActorSystem _client = null!;
        private ActorSystem _server = null!;
        private IActorRef _echo = null!;

        private Config SystemConfig() => ConfigurationFactory.ParseString($$"""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.numeric-refs = {{NumericRefs}}
            akka.log-dead-letters = off
            akka.loglevel = ERROR
            """);

        [GlobalSetup]
        public async Task Setup()
        {
            _server = ActorSystem.Create("numeric-ask-server", SystemConfig());
            _client = ActorSystem.Create("numeric-ask-client", SystemConfig());
            _server.ActorOf(Props.Create(() => new Echo()), "echo");

            var address = ((RemoteActorRefProvider)((ExtendedActorSystem)_server).Provider).DefaultAddress;
            _echo = await _client.ActorSelection($"{address}/user/echo").ResolveOne(Timeout);

            for (var i = 0; i < 1000; i++)
                await _echo.Ask<string>("warm", Timeout);
        }

        [GlobalCleanup]
        public async Task Cleanup()
        {
            await Task.WhenAll(_client.Terminate(), _server.Terminate());
        }

        /// <summary>One Ask at a time: round-trip latency.</summary>
        [Benchmark]
        public Task<string> Ask() => _echo.Ask<string>("ping", Timeout);

        /// <summary>64 Asks in flight: the per-Ask cost under load.</summary>
        [Benchmark(OperationsPerInvoke = Concurrent)]
        public async Task AskConcurrent()
        {
            var tasks = new Task<string>[Concurrent];
            for (var i = 0; i < Concurrent; i++)
                tasks[i] = _echo.Ask<string>("ping", Timeout);
            await Task.WhenAll(tasks);
        }
    }
}
