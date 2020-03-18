using System;
using System.Collections.Generic;
using System.Configuration;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Sql.Common;
using Akka.Persistence.Sqlite.Journal;
using Akka.Persistence.Sqlite;
using Akka.Util.Internal;

namespace AppConfig
{
    class Program
    {
        static void Main(string[] args)
        {
            var system = ActorSystem.Create(Guid.NewGuid().ToString(), ConfigurationFactory.Load());
            SqlitePersistence.Get(system);
            var config = system.Settings.Config.GetConfig("akka.persistence.journal.sqlite");
            var setup = new BatchingSqliteJournalSetup(config);

            Console.WriteLine("If running properly, BatchingSqliteJournalSetup.ConnectionString should return 'myDB://MyConnectionString'.");
            Console.WriteLine($"Connection string is: {setup.ConnectionString}");
        }
    }
}
