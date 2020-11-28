using System.Collections.Immutable;

namespace Akka.Persistence.Sql.Linq2Db.Query.InternalProtocol
{
    public class NewOrderingIds
    {
        public long MaxOrdering { get; }
        public IImmutableList<long> Elements { get; set; }
        
        public NewOrderingIds(long currentMaxOrdering, IImmutableList<long> res)
        {
            MaxOrdering = currentMaxOrdering;
            Elements = res;
        }
    }
}