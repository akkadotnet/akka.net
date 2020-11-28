using System.Collections.Generic;
using System.Threading.Tasks;
using LanguageExt;

namespace Akka.Persistence.Sql.Linq2Db.Journal.Types
{
    public class WriteQueueSet
    {
        public WriteQueueSet(List<TaskCompletionSource<NotUsed>> tcs,
            Seq<JournalRow> rows)
        {
            TCS = tcs;
            Rows = rows;
        }

        public Seq<JournalRow> Rows { get; set; }

        public List<TaskCompletionSource<NotUsed>> TCS { get; }
    }
}