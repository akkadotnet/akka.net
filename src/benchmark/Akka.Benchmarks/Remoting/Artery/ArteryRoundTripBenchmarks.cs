//-----------------------------------------------------------------------
// <copyright file="ArteryRoundTripBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Remoting.Artery
{
    /// <summary>
    /// Request/reply round trips between two in-process Artery systems over loopback TCP, using
    /// only public API. One operation is one round trip: a request from the client system to an
    /// echo actor on the server system and its reply. <c>Ask</c> uses a new future per request;
    /// the Tell variants use one ordinary client actor that sends the next request when a reply
    /// arrives. "InFlight64" keeps 64 requests outstanding at a time.
    /// </summary>
    [MemoryDiagnoser]
    public class ArteryRoundTripBenchmarks
    {
        private const int Sequential = 2_000;
        private const int Window = 64;
        private const int Windowed = Window * 100;

        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        private sealed class Echo : ReceiveActor
        {
            public Echo() => ReceiveAny(m => Sender.Tell(m));
        }

        private sealed record Run(int Count, int Window, TaskCompletionSource<bool> Done);

        /// <summary>Sends <see cref="Run.Count"/> requests to the echo actor, at most <see cref="Run.Window"/> outstanding.</summary>
        private sealed class Pinger : ReceiveActor
        {
            private readonly IActorRef _echo;
            private int _toSend;
            private int _outstanding;
            private TaskCompletionSource<bool>? _done;

            public Pinger(IActorRef echo)
            {
                _echo = echo;
                Receive<Run>(run =>
                {
                    _toSend = run.Count;
                    _done = run.Done;
                    for (var i = 0; i < run.Window && _toSend > 0; i++)
                        SendOne();
                });
                Receive<string>(_ =>
                {
                    _outstanding--;
                    if (_toSend > 0)
                        SendOne();
                    else if (_outstanding == 0)
                        _done!.TrySetResult(true);
                });
            }

            private void SendOne()
            {
                _toSend--;
                _outstanding++;
                _echo.Tell("ping");
            }
        }

        private ActorSystem _client = null!;
        private ActorSystem _server = null!;
        private IActorRef _echo = null!;
        private IActorRef _pinger = null!;

        private static ActorSystem CreateSystem(string name) => ActorSystem.Create(name, ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.log-dead-letters = off
            akka.loglevel = ERROR
            """));

        [GlobalSetup]
        public async Task Setup()
        {
            _server = CreateSystem("round-trip-server");
            _client = CreateSystem("round-trip-client");
            _server.ActorOf(Props.Create(() => new Echo()), "echo");

            var serverAddress = RARP.For(_server).Provider.DefaultAddress;
            _echo = await _client.ActorSelection(new RootActorPath(serverAddress) / "user" / "echo").ResolveOne(Timeout);
            _pinger = _client.ActorOf(Props.Create(() => new Pinger(_echo)), "pinger");

            // Warm the association, both directions and every code path before measuring.
            await AskInFlight64();
            await TellReplyInFlight64();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            var client = _client.Terminate();
            var server = _server.Terminate();
            if (!Task.WaitAll(new[] { client, server }, TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The actor systems did not terminate within 10 seconds.");
        }

        /// <summary>One Ask at a time.</summary>
        [Benchmark(OperationsPerInvoke = Sequential)]
        public async Task AskSequential()
        {
            for (var i = 0; i < Sequential; i++)
                await _echo.Ask<string>("ping", Timeout);
        }

        /// <summary>64 Asks outstanding at a time.</summary>
        [Benchmark(OperationsPerInvoke = Windowed)]
        public Task AskInFlight64()
        {
            var loops = new Task[Window];
            for (var w = 0; w < Window; w++)
                loops[w] = AskLoop(Windowed / Window);
            return Task.WhenAll(loops);
        }

        private async Task AskLoop(int count)
        {
            for (var i = 0; i < count; i++)
                await _echo.Ask<string>("ping", Timeout);
        }

        /// <summary>Tell from an ordinary actor and its reply, one at a time.</summary>
        [Benchmark(OperationsPerInvoke = Sequential)]
        public Task TellReplySequential() => TellReply(Sequential, 1);

        /// <summary>Tell from an ordinary actor and its reply, 64 outstanding at a time.</summary>
        [Benchmark(OperationsPerInvoke = Windowed)]
        public Task TellReplyInFlight64() => TellReply(Windowed, Window);

        private async Task TellReply(int count, int window)
        {
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pinger.Tell(new Run(count, window, done));
            if (await Task.WhenAny(done.Task, Task.Delay(Timeout)) != done.Task)
                throw new TimeoutException($"{count} Tell round trips did not complete within {Timeout}.");
        }
    }
}
