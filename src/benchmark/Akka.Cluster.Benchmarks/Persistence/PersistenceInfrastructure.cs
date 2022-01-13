//-----------------------------------------------------------------------
// <copyright file="PersistenceInfrastructure.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Util;
using Akka.Configuration;
using Akka.Util.Internal;
using Akka.Persistence;

namespace Akka.Cluster.Benchmarks.Persistence
{
public sealed class Init
    {
        public static readonly Init Instance = new Init();
        private Init() { }
    }

    public sealed class Finish
    {
        public static readonly Finish Instance = new Finish();
        private Finish() { }
    }
    public sealed class Done
    {
        public static readonly Done Instance = new Done();
        private Done() { }
    }
    public sealed class Finished
    {
        public readonly long State;

        public Finished(long state)
        {
            State = state;
        }
    }

    public sealed class Store
    {
        public readonly int Value;

        public Store(int value)
        {
            Value = value;
        }
    }

    public sealed class Stored
    {
        public readonly int Value;

        public Stored(int value)
        {
            Value = value;
        }
    }

    /// <summary>
    /// Query to <see cref="BenchmarkDoneActor"/> that will be used to signal termination
    /// </summary>
    public sealed class IsFinished
    {
        public static readonly IsFinished Instance = new IsFinished();
        private IsFinished(){}
    }

    public sealed class BenchmarkDoneActor : ReceiveActor
    {
        private int _expected;
        private IActorRef _asker;

        public BenchmarkDoneActor(int expected)
        {
            _expected = expected;

            Receive<IsFinished>(_ =>
            {
                _asker = Sender;
            });

            Receive<Finished>(f =>
            {
                // this will terminate the benchmark
                if(--_expected <= 0)
                    _asker.Tell(Done.Instance);
            });
        }
    }

    public sealed class PerformanceTestActor : PersistentActor
    {
        private long _state = 0L;
        private readonly long _target;
        private readonly IActorRef _doneActor;
        public PerformanceTestActor(string persistenceId, IActorRef doneActor, long target)
        {
            _doneActor = doneActor;
            PersistenceId = persistenceId;
            _target = target;
        }

        public sealed override string PersistenceId { get; }

        protected override bool ReceiveRecover(object message) {
            switch(message){
                case Stored s:
                    _state += s.Value;
                break;
                default:
                    return false;
            }

            return true;
        }

        protected override bool ReceiveCommand(object message){
            switch(message){
                case Store store:
                    PersistAsync(new Stored(store.Value), s =>
                    {
                        _state += s.Value;
                        if(_state >= _target)
                            _doneActor.Tell(new Finished(_state));
                    });
                    break;
                case Init _:
                    var sender = Sender;
                    PersistAsync(new Stored(0), s =>
                    {
                        _state += s.Value;
                        sender.Tell(Done.Instance);
                    });
                    break;
                case Finish _:
                    Sender.Tell(new Finished(_state));
                    break;
                default:
                    return false;
            }

            return true;
        }
    }


    public static class PersistenceInfrastructure{
        public static readonly AtomicCounter DbCounter = new AtomicCounter(0);

        public static (string connectionString, Config hoconConfig) GenerateJournalConfig(){
            return GenerateJournalConfig(DbCounter.GetAndIncrement().ToString());
        }

        public static (string connectionString, Config hoconConfig) GenerateJournalConfig(string databaseId){
            // need to create a unique database instance each time benchmark is run so we don't pollute
            // might need to disable shared cache
            var connectionString = $"Datasource=memdb-journal-{databaseId}.db;Mode=Memory;Cache=Shared";

            var config = ConfigurationFactory.ParseString(@"
            akka {
                persistence.journal {
                    plugin = ""akka.persistence.journal.sqlite""
                    sqlite {
                        class = ""Akka.Persistence.Sqlite.Journal.BatchingSqliteJournal, Akka.Persistence.Sqlite""
                        plugin-dispatcher = ""akka.actor.default-dispatcher""
                        table-name = event_journal
                        metadata-table-name = journal_metadata
                        auto-initialize = on
                        connection-string = """+ connectionString +@"""
                    }
                }
            }");

            return (connectionString, config);
        } 
    }
    
}