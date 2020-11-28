using System.Threading.Tasks;
using LanguageExt;

namespace Akka.Persistence.Sql.Linq2Db.Journal.Types
{
    public class WriteQueueEntry
    {
        public WriteQueueEntry(TaskCompletionSource<NotUsed> tcs,
            Seq<JournalRow> rows)
        {
            TCS = tcs;
            Rows = rows;
        }

        public Seq<JournalRow> Rows { get; }

        public TaskCompletionSource<NotUsed> TCS { get; }
    }
}