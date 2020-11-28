using Akka.Actor;
using Akka.Persistence.Query;

namespace Akka.Persistence.Sql.Linq2Db.Query
{
    public class Linq2DbReadJournalProvider : IReadJournalProvider
    {
        private string _configPath;
        private ExtendedActorSystem _system;
        private Configuration.Config _config;

        public Linq2DbReadJournalProvider(ExtendedActorSystem system,
            Configuration.Config config)
        {
            _system = system;
            _config = config;
            _configPath = "linq2db";
        }

        public Linq2DbReadJournalProvider(ExtendedActorSystem system,
            Configuration.Config config, string configPath)
        {
            _system = system;
            _config = config;
            _configPath = configPath;
        }

        public IReadJournal GetReadJournal()
        {
            return new Linq2DbReadJournal(_system, _config, _configPath);
        }
    }
}