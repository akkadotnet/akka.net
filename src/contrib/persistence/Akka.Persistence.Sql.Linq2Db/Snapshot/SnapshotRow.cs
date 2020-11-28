using Akka.Actor;
using LinqToDB.Mapping;
using System;
using LinqToDB;

namespace Akka.Persistence.Sql.Linq2Db.Snapshot
{

    
    public class SnapshotRow
    {
        [PrimaryKey]
        [NotNull]
        public string PersistenceId { get; set; }
        [PrimaryKey]
        public long SequenceNumber { get; set; }
        [Column(DataType = DataType.DateTime2)]
        public DateTime Created { get; set; }
        public byte[] Payload { get; set; }
        public string Manifest { get; set; }
        public int? SerializerId { get; set; }
    }
}