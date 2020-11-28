using System.Collections.Immutable;
using Akka.Streams.Dsl;
using Akka.Util;

namespace Akka.Persistence.Sql.Linq2Db.Serialization
{
    public abstract class
        FlowPersistentReprSerializer<T> : PersistentReprSerializer<T>
    {
        public
            Flow<T, Try<(IPersistentRepresentation, IImmutableSet<string>, long)>
                , NotUsed> DeserializeFlow()
        {
            return Flow.Create<T, NotUsed>().Select(t => Deserialize(t));
            //.Select(t => );

        }
    }
}