using LinqToDB;
using LinqToDB.Mapping;

namespace Akka.Persistence.Sql.Linq2Db.Journal.Types
{
    public sealed class JournalRow
    {
        //[Column(Configuration = ProviderName.SQLite, DbType = "INTEGER", IsIdentity = true, IsPrimaryKey = true)]
        //[Column(IsIdentity = true, IsPrimaryKey = false)]
        public long ordering { get; set; }
        
        public long Timestamp { get; set; } = 0;

        public bool deleted { get; set; }
        //[Column(Configuration = ProviderName.SQLite, IsPrimaryKey = false)]
        //[Column(IsPrimaryKey = true, CanBeNull = false)]
        public string persistenceId { get; set; }
        //[Column(Configuration = ProviderName.SQLite, IsPrimaryKey = false)]
        //[Column(IsPrimaryKey = true)]
        public long sequenceNumber { get; set; }
        [Column(CanBeNull = false)]
        public byte[] message { get; set; }
        public string tags { get; set; }
        public string manifest { get; set; }
        public int? Identifier { get; set; }
    }
}