//-----------------------------------------------------------------------
// <copyright file="MemoryJournalBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Configuration;
using Akka.Persistence;
using Akka.Persistence.Journal;
using BenchmarkDotNet.Attributes;
using static Akka.Benchmarks.Configurations.BenchmarkCategories;

namespace Akka.Benchmarks.Persistence
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class MemoryJournalBenchmarks
    {
        private static readonly Config Config = ConfigurationFactory.ParseString(@"
            akka.persistence.journal.plugin = ""akka.persistence.journal.inmem""
            akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.inmem""
        ");

        private ActorSystem _system;
        private IActorRef _persistentActor;

        [Params(10, 100, 1000)]
        public int EventCount { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _system = ActorSystem.Create("benchmark", Config);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _system?.Dispose();
        }

        [IterationSetup]
        public void IterationSetup()
        {
            _persistentActor = _system.ActorOf(Props.Create(() => new BenchmarkPersistentActor($"benchmark-{Guid.NewGuid()}")));
        }

        [IterationCleanup]
        public void IterationCleanup()
        {
            _persistentActor?.GracefulStop(TimeSpan.FromSeconds(5)).Wait();
        }

        [Benchmark]
        [BenchmarkCategory(MicroBenchmark)]
        public async Task Write_events_to_memory_journal()
        {
            for (var i = 0; i < EventCount; i++)
            {
                await _persistentActor.Ask<string>($"event-{i}", TimeSpan.FromSeconds(10));
            }
        }

        [Benchmark]
        [BenchmarkCategory(MicroBenchmark)]
        public async Task Write_and_replay_events()
        {
            // Write events
            for (var i = 0; i < EventCount; i++)
            {
                await _persistentActor.Ask<string>($"event-{i}", TimeSpan.FromSeconds(10));
            }

            // Trigger recovery by stopping and restarting
            var persistenceId = await _persistentActor.Ask<string>("get-id", TimeSpan.FromSeconds(5));
            await _persistentActor.GracefulStop(TimeSpan.FromSeconds(5));

            _persistentActor = _system.ActorOf(Props.Create(() => new BenchmarkPersistentActor(persistenceId)));

            // Wait for recovery to complete
            await _persistentActor.Ask<int>("get-count", TimeSpan.FromSeconds(10));
        }

        [Benchmark]
        [BenchmarkCategory(MicroBenchmark)]
        public async Task Write_tagged_events_and_query()
        {
            // Write tagged events
            for (var i = 0; i < EventCount; i++)
            {
                await _persistentActor.Ask<string>($"tagged-event-{i}", TimeSpan.FromSeconds(10));
            }

            // Query by tag (simulated via replay)
            var persistenceId = await _persistentActor.Ask<string>("get-id", TimeSpan.FromSeconds(5));
            await _persistentActor.GracefulStop(TimeSpan.FromSeconds(5));

            _persistentActor = _system.ActorOf(Props.Create(() => new BenchmarkPersistentActor(persistenceId)));
            await _persistentActor.Ask<int>("get-count", TimeSpan.FromSeconds(10));
        }

        #region Test Actor

        private class BenchmarkPersistentActor : ReceivePersistentActor
        {
            private int _eventCount;

            public BenchmarkPersistentActor(string persistenceId)
            {
                PersistenceId = persistenceId;

                Command<string>(msg =>
                {
                    switch (msg)
                    {
                        case "get-count":
                            Sender.Tell(_eventCount);
                            break;
                        case "get-id":
                            Sender.Tell(PersistenceId);
                            break;
                        default:
                        {
                            if (msg.StartsWith("tagged-event-"))
                            {
                                var tagged = new Tagged(msg, new[] { "benchmark-tag" });
                                Persist(tagged, _ =>
                                {
                                    _eventCount++;
                                    Sender.Tell(msg);
                                });
                            }
                            else
                            {
                                Persist(msg, _ =>
                                {
                                    _eventCount++;
                                    Sender.Tell(msg);
                                });
                            }

                            break;
                        }
                    }
                });

                Recover<string>(msg =>
                {
                    _eventCount++;
                });

                Recover<Tagged>(tagged =>
                {
                    _eventCount++;
                });
            }

            public override string PersistenceId { get; }
        }

        #endregion
    }
}
