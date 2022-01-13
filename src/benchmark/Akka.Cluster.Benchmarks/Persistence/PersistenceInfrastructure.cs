//-----------------------------------------------------------------------
// <copyright file="PersistenceInfrastructure.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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

    public sealed class PerformanceTestActor : PersistentActor
    {
        private long state = 0L;
        public PerformanceTestActor(string persistenceId)
        {
            PersistenceId = persistenceId;
        }

        public sealed override string PersistenceId { get; }

        protected override bool ReceiveRecover(object message) {
            switch(message){
                case Stored s:
                    state += s.Value;
                break;
                default:
                    return false;
            }

            return true;
        }

        protected override bool ReceiveCommand(object message){
            switch(message){
                case Store store:
                     Persist(new Stored(store.Value), s =>
                    {
                        state += s.Value;
                    });
                    break;
                case Init _:
                    var sender = Sender;
                    Persist(new Stored(0), s =>
                    {
                        state += s.Value;
                        sender.Tell(Done.Instance);
                    });
                    break;
                case Finish _:
                    Sender.Tell(new Finished(state));
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