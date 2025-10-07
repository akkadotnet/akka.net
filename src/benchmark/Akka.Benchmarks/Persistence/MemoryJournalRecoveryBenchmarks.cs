//-----------------------------------------------------------------------
// <copyright file="MemoryJournalRecoveryBenchmarks.cs" company="Akka.NET Project">
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
    public class MemoryJournalRecoveryBenchmarks
    {
        private static readonly Config Config = ConfigurationFactory.ParseString(@"
            akka.persistence.journal.plugin = ""akka.persistence.journal.inmem""
            akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.inmem""
        ");

        private ActorSystem _system;
        private IActorRef _persistentActor;
        private string _persistenceId;

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
            // Generate unique persistence ID for this iteration
            _persistenceId = $"benchmark-{Guid.NewGuid()}";

            // Pre-populate the journal with events
            var prepActor = _system.ActorOf(Props.Create(() => new BenchmarkPersistentActor(_persistenceId)));
            for (var i = 0; i < EventCount; i++)
            {
                prepActor.Ask<string>($"event-{i}", TimeSpan.FromSeconds(10)).Wait();
            }
            prepActor.GracefulStop(TimeSpan.FromSeconds(5)).Wait();

            // Create the actor that will recover (but don't wait for recovery yet)
            _persistentActor = _system.ActorOf(Props.Create(() => new BenchmarkPersistentActor(_persistenceId)));
        }

        [IterationCleanup]
        public void IterationCleanup()
        {
            _persistentActor?.GracefulStop(TimeSpan.FromSeconds(5)).Wait();
        }

        [Benchmark]
        [BenchmarkCategory(MicroBenchmark)]
        public async Task Recover_events_from_memory_journal()
        {
            // Wait for recovery to complete by asking for the event count
            var count = await _persistentActor.Ask<int>("get-count", TimeSpan.FromSeconds(10));
            if (count != EventCount)
            {
                throw new Exception($"Expected {EventCount} events, but got {count}");
            }
        }

        [IterationSetup(Target = nameof(Recover_tagged_events_from_memory_journal))]
        public void IterationSetupTagged()
        {
            // Generate unique persistence ID for this iteration
            _persistenceId = $"benchmark-{Guid.NewGuid()}";

            // Pre-populate the journal with tagged events
            var prepActor = _system.ActorOf(Props.Create(() => new BenchmarkPersistentActor(_persistenceId)));
            for (var i = 0; i < EventCount; i++)
            {
                prepActor.Ask<string>($"tagged-event-{i}", TimeSpan.FromSeconds(10)).Wait();
            }
            prepActor.GracefulStop(TimeSpan.FromSeconds(5)).Wait();

            // Create the actor that will recover (but don't wait for recovery yet)
            _persistentActor = _system.ActorOf(Props.Create(() => new BenchmarkPersistentActor(_persistenceId)));
        }

        [Benchmark]
        [BenchmarkCategory(MicroBenchmark)]
        public async Task Recover_tagged_events_from_memory_journal()
        {
            // Wait for recovery to complete by asking for the event count
            var count = await _persistentActor.Ask<int>("get-count", TimeSpan.FromSeconds(10));
            if (count != EventCount)
            {
                throw new Exception($"Expected {EventCount} events, but got {count}");
            }
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
