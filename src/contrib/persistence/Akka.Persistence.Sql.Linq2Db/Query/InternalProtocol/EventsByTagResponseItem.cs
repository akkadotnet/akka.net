using System.Collections.Immutable;

namespace Akka.Persistence.Sql.Linq2Db.Query.InternalProtocol
{
    public class EventsByTagResponseItem
    {
        public IPersistentRepresentation Repr { get; set; }
        public ImmutableHashSet<string> Tags { get; set; }
        public long SequenceNr { get; set; }
    }
}