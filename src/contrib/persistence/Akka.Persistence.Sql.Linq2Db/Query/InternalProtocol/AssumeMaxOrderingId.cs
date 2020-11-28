namespace Akka.Persistence.Sql.Linq2Db.Query.InternalProtocol
{
    public class AssumeMaxOrderingId
    {
        public AssumeMaxOrderingId(long max)
        {
            Max = max;
        }

        public long Max { get; set; }
    }
}