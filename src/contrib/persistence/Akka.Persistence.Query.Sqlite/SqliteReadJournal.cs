using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query.Sql;

namespace Akka.Persistence.Query.Sqlite
{
    public class SqliteReadJournal : SqlReadJournal
    {
        public new static string Identifier = "akka.persistence.query.journal.sqlite";
        
        public SqliteReadJournal(ExtendedActorSystem system, Config config)
            : base(system, config)
        {
        }
        
        public new static Config DefaultConfiguration()
        {
            return ConfigurationFactory.FromResource<SqliteReadJournal>("Akka.Persistence.Query.Sqlite.reference.conf");
        }
    }
}