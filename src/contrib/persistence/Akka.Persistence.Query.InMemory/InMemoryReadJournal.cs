using Akka.Configuration;
using Akka.Streams.Dsl;

namespace Akka.Persistence.Query.InMemory
{
    public class InMemoryReadJournal :
        ICurrentPersistenceIdsQuery
    {
        public const string Identifier = "akka.persistence.query.journal.inmem";
        private readonly string _writeJournalPluginId;
        
        public static Config DefaultConfiguration()
        {
            return ConfigurationFactory.FromResource<InMemoryReadJournal>("Akka.Persistence.Query.InMemory.reference.conf");
        }
        
        public InMemoryReadJournal(Config config)
        {
            _writeJournalPluginId = config.GetString("write-plugin", null);
        }
        
        public Source<string, NotUsed> CurrentPersistenceIds()
        {
            return Source.ActorPublisher<string>(CurrentPersistenceIdsPublisher.Props(_writeJournalPluginId))
                .MapMaterializedValue(_ => NotUsed.Instance)
                .Named("CurrentPersistenceIds");
        }
    }
}