//-----------------------------------------------------------------------
// <copyright file="ReceiveActorDispatchBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Benchmarks.Dispatch;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Actor
{
    /// <summary>
    /// The cost of getting one message to its handler in a <see cref="ReceiveActor"/> with eight
    /// <c>Receive&lt;T&gt;</c> handlers for distinct message types, the messages cycling through all
    /// eight types. The actor runs on a calling-thread dispatcher, so each <c>Tell</c> is processed on
    /// the benchmark thread before it returns and no thread hand-off is measured.
    /// </summary>
    [Config(typeof(MicroBenchmarkConfig))]
    public class ReceiveActorDispatchBenchmarks
    {
        private const int Messages = 8_000;

        private sealed class M0 { }
        private sealed class M1 { }
        private sealed class M2 { }
        private sealed class M3 { }
        private sealed class M4 { }
        private sealed class M5 { }
        private sealed class M6 { }
        private sealed class M7 { }

        private sealed class EightHandlers : ReceiveActor
        {
            public static long Handled;

            public EightHandlers()
            {
                Receive<M0>(_ => Handled++);
                Receive<M1>(_ => Handled++);
                Receive<M2>(_ => Handled++);
                Receive<M3>(_ => Handled++);
                Receive<M4>(_ => Handled++);
                Receive<M5>(_ => Handled++);
                Receive<M6>(_ => Handled++);
                Receive<M7>(_ => Handled++);
            }
        }

        private static readonly object[] Cycle =
        {
            new M0(), new M1(), new M2(), new M3(), new M4(), new M5(), new M6(), new M7()
        };

        private ActorSystem _system;
        private IActorRef _actor;

        [GlobalSetup]
        public async Task Setup()
        {
            _system = ActorSystem.Create("receive-dispatch", MailboxThroughputBenchmarks.Config);
            _actor = _system.ActorOf(Props.Create(() => new EightHandlers()).WithDispatcher("calling-thread-dispatcher"));
            await _actor.Ask<ActorIdentity>(new Identify(null), TimeSpan.FromSeconds(10));

            // The actor is started and its mailbox runs on the calling thread: a Tell has been handled when it returns.
            var before = EightHandlers.Handled;
            _actor.Tell(Cycle[0]);
            if (EightHandlers.Handled != before + 1)
                throw new InvalidOperationException("The actor did not handle the message on the calling thread.");
        }

        [GlobalCleanup]
        public void Cleanup() => _system.Terminate().Wait(TimeSpan.FromSeconds(10));

        [Benchmark(OperationsPerInvoke = Messages)]
        public void EightHandlersCycling()
        {
            var actor = _actor;
            for (var i = 0; i < Messages; i++)
                actor.Tell(Cycle[i & 7], ActorRefs.NoSender);
        }
    }
}
