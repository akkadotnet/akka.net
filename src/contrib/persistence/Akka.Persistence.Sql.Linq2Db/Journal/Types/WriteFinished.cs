using System.Threading.Tasks;

namespace Akka.Persistence.Sql.Linq2Db.Journal.Types
{
    public class WriteFinished
    {
        public WriteFinished(string persistenceId, Task future)
        {
            PersistenceId = persistenceId;
            Future = future;
        }
        public string PersistenceId { get; protected set; }
        public Task Future { get; protected set; }
    }
}