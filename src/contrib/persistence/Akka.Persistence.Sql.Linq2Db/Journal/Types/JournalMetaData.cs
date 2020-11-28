using LinqToDB.Mapping;

namespace Akka.Persistence.Sql.Linq2Db.Journal.Types
{
    public sealed class JournalMetaData
    {
        [Column(IsPrimaryKey = true, CanBeNull = false)]
        public string PersistenceId { get; set; }
        [PrimaryKey]
        public long SequenceNumber { get; set; }
    }
}