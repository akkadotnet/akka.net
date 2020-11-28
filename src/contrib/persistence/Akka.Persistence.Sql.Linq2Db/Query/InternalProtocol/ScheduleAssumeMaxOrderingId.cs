namespace Akka.Persistence.Sql.Linq2Db.Query.InternalProtocol
{
    public class ScheduleAssumeMaxOrderingId
    {
        public ScheduleAssumeMaxOrderingId(long maxInDatabase)
        {
            MaxInDatabase = maxInDatabase;
        }

        public long MaxInDatabase { get; set; }
    }
}